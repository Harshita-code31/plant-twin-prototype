import * as THREE from "three";
import { OrbitControls } from "three/addons/OrbitControls.js";
import { EQUIPMENT } from "./scenarios.js";

// The K-101 compressor skid, built from primitives so the prototype has no model files to
// load. Each tappable piece of equipment is a THREE.Group tagged with its equipment id; swap a
// group's children for an imported GLB later and picking, labels and highlighting still work.

const V = (x, y, z) => new THREE.Vector3(x, y, z);
const WIND = V(1.1, 0, 0); // blowing from Muster A (west, -x) towards Muster B (east, +x)

const FLANGE_POS = V(0, 2.9, 1.1);
const STACK_TIP = V(1.2, 7.6, 2.2);

function mat(color, opts = {}) {
  return new THREE.MeshStandardMaterial({ color, roughness: 0.72, metalness: 0.2, ...opts });
}

function cylBetween(a, b, r, material, segments = 16) {
  const dir = new THREE.Vector3().subVectors(b, a);
  const len = dir.length();
  const mesh = new THREE.Mesh(new THREE.CylinderGeometry(r, r, len, segments), material);
  mesh.position.copy(a).addScaledVector(dir, 0.5);
  mesh.quaternion.setFromUnitVectors(V(0, 1, 0), dir.normalize());
  return mesh;
}

function pipeRun(group, points, r, material) {
  for (let i = 0; i < points.length - 1; i++) group.add(cylBetween(points[i], points[i + 1], r, material));
  for (let i = 1; i < points.length - 1; i++) {
    const elbow = new THREE.Mesh(new THREE.SphereGeometry(r * 1.12, 16, 12), material);
    elbow.position.copy(points[i]);
    group.add(elbow);
  }
}

function box(w, h, d, material, x, y, z) {
  const m = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), material);
  m.position.set(x, y, z);
  return m;
}

function textTexture(lines, { bg = "#1f6b3a", fg = "#ffffff", w = 256, h = 128, size = 44 } = {}) {
  const c = document.createElement("canvas");
  c.width = w;
  c.height = h;
  const ctx = c.getContext("2d");
  const draw = (ls, b = bg, f = fg) => {
    ctx.fillStyle = b;
    ctx.fillRect(0, 0, w, h);
    ctx.fillStyle = f;
    ctx.textAlign = "center";
    ctx.textBaseline = "middle";
    ctx.font = `600 ${size}px "Barlow Condensed", "Arial Narrow", sans-serif`;
    const step = h / (ls.length + 1);
    ls.forEach((l, i) => ctx.fillText(l, w / 2, step * (i + 1)));
  };
  draw(lines);
  const tex = new THREE.CanvasTexture(c);
  tex.colorSpace = THREE.SRGBColorSpace;
  tex.redraw = (ls, b, f) => {
    draw(ls, b, f);
    tex.needsUpdate = true;
  };
  return tex;
}

function dotTexture() {
  const c = document.createElement("canvas");
  c.width = c.height = 64;
  const ctx = c.getContext("2d");
  const g = ctx.createRadialGradient(32, 32, 0, 32, 32, 32);
  g.addColorStop(0, "rgba(255,255,255,1)");
  g.addColorStop(0.5, "rgba(255,255,255,0.45)");
  g.addColorStop(1, "rgba(255,255,255,0)");
  ctx.fillStyle = g;
  ctx.fillRect(0, 0, 64, 64);
  const tex = new THREE.CanvasTexture(c);
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

/** A simple particle stream: a fixed pool of points, recycled as they age out. */
class Plume {
  constructor(scene, { count, color, size, opacity, life }) {
    this.count = count;
    this.life = life;
    this.pos = new Float32Array(count * 3).fill(-999);
    this.vel = new Float32Array(count * 3);
    this.age = new Float32Array(count).fill(Infinity);
    this.cursor = 0;
    this.carry = 0;
    const geo = new THREE.BufferGeometry();
    geo.setAttribute("position", new THREE.BufferAttribute(this.pos, 3));
    this.material = new THREE.PointsMaterial({
      color, size, transparent: true, opacity, depthWrite: false, map: dotTexture(), sizeAttenuation: true,
    });
    this.points = new THREE.Points(geo, this.material);
    this.points.frustumCulled = false;
    scene.add(this.points);
  }

  emit(origin, baseVel, spread) {
    const i = this.cursor;
    this.cursor = (this.cursor + 1) % this.count;
    this.pos.set([origin.x, origin.y, origin.z], i * 3);
    this.vel.set([
      baseVel.x + (Math.random() - 0.5) * spread,
      baseVel.y + (Math.random() - 0.5) * spread,
      baseVel.z + (Math.random() - 0.5) * spread,
    ], i * 3);
    this.age[i] = 0;
  }

  update(dt, rate, origin, baseVel, spread, lift) {
    this.carry += rate * dt;
    while (this.carry >= 1) {
      this.emit(origin, baseVel, spread);
      this.carry -= 1;
    }
    const drag = Math.exp(-dt * 1.2);
    for (let i = 0; i < this.count; i++) {
      if (this.age[i] === Infinity) continue;
      this.age[i] += dt;
      const k = i * 3;
      if (this.age[i] > this.life) {
        this.age[i] = Infinity;
        this.pos[k + 1] = -999;
        continue;
      }
      this.vel[k] = this.vel[k] * drag + WIND.x * (1 - drag);
      this.vel[k + 1] = this.vel[k + 1] * drag + lift * (1 - drag);
      this.vel[k + 2] = this.vel[k + 2] * drag + WIND.z * (1 - drag);
      this.pos[k] += this.vel[k] * dt;
      this.pos[k + 1] += this.vel[k + 1] * dt;
      this.pos[k + 2] += this.vel[k + 2] * dt;
    }
    this.points.geometry.attributes.position.needsUpdate = true;
  }

  clear() {
    this.age.fill(Infinity);
    this.pos.fill(-999);
    this.carry = 0;
    this.points.geometry.attributes.position.needsUpdate = true;
  }
}

export class Twin {
  constructor(host, labelLayer, onPick) {
    this.host = host;
    this.labelLayer = labelLayer;
    this.onPick = onPick;
    this.targets = new Map(); // id -> { group, anchor, meshes, label }
    this.pickables = [];
    this.selected = null;
    this.time = 0;

    this.renderer = new THREE.WebGLRenderer({ antialias: true });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    host.prepend(this.renderer.domElement);

    this.scene = new THREE.Scene();
    this.camera = new THREE.PerspectiveCamera(45, 1, 0.1, 200);
    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.enableDamping = true;
    this.controls.minDistance = 5;
    this.controls.maxDistance = 34;
    this.controls.maxPolarAngle = 1.45;
    this.controls.screenSpacePanning = true;

    this.build();
    this.applyTheme();
    this.resetView();
    this.bindPicking();

    this.resizeObserver = new ResizeObserver(() => this.resize());
    this.resizeObserver.observe(host);
    this.resize();
  }

  // ---------------------------------------------------------------- scene
  build() {
    const scene = this.scene;
    this.hemi = new THREE.HemisphereLight(0xdfe7ec, 0x5d6468, 1.1);
    scene.add(this.hemi);
    const sun = new THREE.DirectionalLight(0xffffff, 1.6);
    sun.position.set(9, 16, 7);
    sun.castShadow = true;
    sun.shadow.mapSize.set(1024, 1024);
    Object.assign(sun.shadow.camera, { left: -15, right: 15, top: 15, bottom: -15, near: 1, far: 50 });
    scene.add(sun);

    this.groundMat = mat(0x8f999e, { roughness: 1, metalness: 0 });
    const ground = new THREE.Mesh(new THREE.PlaneGeometry(160, 160), this.groundMat);
    ground.rotation.x = -Math.PI / 2;
    ground.receiveShadow = true;
    scene.add(ground);

    const pad = new THREE.Mesh(new THREE.PlaneGeometry(26, 15), mat(0xb3b8ba, { roughness: 0.95, metalness: 0 }));
    pad.rotation.x = -Math.PI / 2;
    pad.position.set(0, 0.01, 1);
    pad.receiveShadow = true;
    scene.add(pad);

    // Painted walkway towards each muster point.
    const paint = mat(0xd9b23a, { roughness: 0.9, metalness: 0 });
    for (const x of [-7.2, 7.2]) {
      const stripe = new THREE.Mesh(new THREE.PlaneGeometry(5.6, 0.12), paint);
      stripe.rotation.x = -Math.PI / 2;
      stripe.position.set(x, 0.02, 5.2);
      scene.add(stripe);
    }

    const steel = mat(0x6c757b, { metalness: 0.5 });
    const skidMat = mat(0xa8862f, { metalness: 0.35 });
    const pipeMat = mat(0x9aa3a8, { metalness: 0.55, roughness: 0.45 });
    const add = (o) => {
      o.traverse((c) => {
        if (c.isMesh) {
          c.castShadow = true;
          c.receiveShadow = true;
        }
      });
      scene.add(o);
      return o;
    };

    // Skid base and static pipework.
    const statics = new THREE.Group();
    statics.add(box(9.6, 0.3, 5.2, skidMat, -0.6, 0.15, 0.2));
    pipeRun(statics, [V(-4.0, 1.3, -0.8), V(-2.2, 1.3, -0.8), V(-2.2, 1.3, 0), V(-1.35, 1.3, 0)], 0.13, pipeMat);
    pipeRun(statics, [V(0, 2.1, 0), V(0, 2.9, 0), V(0, 2.9, 2.2), V(7.5, 2.9, 2.2)], 0.13, pipeMat);
    pipeRun(statics, [V(1.2, 2.9, 2.2), STACK_TIP], 0.11, pipeMat);
    statics.add(cylBetween(V(1.2, 7.6, 2.2), V(1.2, 7.9, 2.2), 0.16, steel));
    // Pipe rack under the header.
    for (const x of [3.6, 6.2]) {
      statics.add(box(0.18, 2.75, 0.18, steel, x, 1.375, 2.2));
    }
    statics.add(box(3.0, 0.16, 0.5, steel, 4.9, 2.72, 2.2));
    // Lube oil lines to the bearings.
    pipeRun(statics, [V(2.2, 1.0, -1.55), V(2.2, 1.0, -0.6), V(1.3, 1.0, -0.6)], 0.04, mat(0x7a5c2e));
    add(statics);

    // Background: storage tanks and the plant flare, so the skid reads as part of a plant.
    const far = new THREE.Group();
    const tankMat = mat(0xc9cdd0, { metalness: 0.3 });
    const placed = (geo, m, p) => {
      const mesh = new THREE.Mesh(geo, m);
      mesh.position.copy(p);
      return mesh;
    };
    far.add(placed(new THREE.CylinderGeometry(4, 4, 6, 32), tankMat, V(-15, 3, -14)));
    far.add(placed(new THREE.CylinderGeometry(3, 3, 5, 32), tankMat, V(-5, 2.5, -16)));
    far.add(placed(new THREE.CylinderGeometry(0.35, 0.5, 22, 12), steel, V(16, 11, -18)));
    add(far);

    // ---- Equipment (tappable) ----
    const g = (id, build) => {
      const group = new THREE.Group();
      group.name = id;
      build(group);
      add(group);
      this.register(id, group);
      return group;
    };

    g("scrubber", (gr) => {
      const m = mat(0x8e9aa2, { metalness: 0.4 });
      const shell = new THREE.Mesh(new THREE.CylinderGeometry(0.62, 0.62, 2.8, 28), m);
      shell.position.set(-4.6, 1.95, -0.8);
      gr.add(shell);
      for (const y of [0.55, 3.35]) {
        const cap = new THREE.Mesh(new THREE.SphereGeometry(0.62, 28, 14), m);
        cap.scale.y = 0.45;
        cap.position.set(-4.6, y, -0.8);
        gr.add(cap);
      }
    });

    g("compressor", (gr) => {
      const m = mat(0x5f7d95, { metalness: 0.45 });
      const casing = cylBetween(V(-1.35, 1.3, 0), V(1.1, 1.3, 0), 0.82, m, 32);
      gr.add(casing);
      gr.add(cylBetween(V(-1.5, 1.3, 0), V(-1.3, 1.3, 0), 0.6, m, 24));
      gr.add(box(2.6, 0.5, 1.4, mat(0x4b5a63), -0.1, 0.55, 0));
      gr.add(cylBetween(V(0, 1.9, 0), V(0, 2.2, 0), 0.22, m));
      this.compressorGroup = gr;
    });
    this.compressorMats = [];
    this.targets.get("compressor").meshes.forEach((mesh) => this.compressorMats.push(mesh.material));

    g("motor", (gr) => {
      const m = mat(0x3f6e57, { metalness: 0.3 });
      gr.add(cylBetween(V(1.65, 1.2, 0), V(3.7, 1.2, 0), 0.7, m, 28));
      for (let x = 1.9; x < 3.6; x += 0.28) gr.add(cylBetween(V(x, 1.2, 0), V(x + 0.06, 1.2, 0), 0.74, m, 28));
      gr.add(box(0.5, 0.4, 0.5, m, 2.7, 2.0, 0));
      gr.add(box(2.4, 0.5, 1.3, mat(0x4b5a63), 2.65, 0.55, 0));
      const coupling = cylBetween(V(1.1, 1.3, 0), V(1.65, 1.3, 0), 0.26, mat(0xd2a531), 8);
      gr.add(coupling);
      this.coupling = coupling;
    });

    g("discharge_flange", (gr) => {
      this.flangeMat = mat(0x9aa3a8, { metalness: 0.6, roughness: 0.4 });
      for (const dz of [-0.05, 0.05]) gr.add(cylBetween(V(0, 2.9, 1.1 + dz - 0.04), V(0, 2.9, 1.1 + dz + 0.04), 0.3, this.flangeMat, 24));
      const boltMat = this.flangeMat;
      for (let i = 0; i < 8; i++) {
        const a = (i / 8) * Math.PI * 2;
        gr.add(cylBetween(V(Math.cos(a) * 0.24, 2.9 + Math.sin(a) * 0.24, 0.98), V(Math.cos(a) * 0.24, 2.9 + Math.sin(a) * 0.24, 1.22), 0.025, boltMat, 6));
      }
    });

    const valve = (id, pos, axis, actuatorColor) =>
      g(id, (gr) => {
        const body = mat(0x6e7b83, { metalness: 0.5 });
        const dir = axis === "x" ? V(1, 0, 0) : axis === "y" ? V(0, 1, 0) : V(0, 0, 1);
        gr.add(cylBetween(pos.clone().addScaledVector(dir, -0.28), pos.clone().addScaledVector(dir, 0.28), 0.2, body));
        const up = axis === "y" ? V(1, 0, 0) : V(0, 1, 0);
        gr.add(cylBetween(pos, pos.clone().addScaledVector(up, 0.5), 0.07, body));
        const act = mat(actuatorColor, { metalness: 0.3 });
        const head = cylBetween(pos.clone().addScaledVector(up, 0.5), pos.clone().addScaledVector(up, 0.85), 0.24, act);
        gr.add(head);
        // Position indicator: green when closed, red when open (field convention on this site).
        const indMat = mat(0xc4271f, { emissive: 0x000000 });
        const ind = new THREE.Mesh(new THREE.SphereGeometry(0.08, 12, 8), indMat);
        ind.position.copy(pos).addScaledVector(up, 0.95);
        gr.add(ind);
        gr.userData.indicator = indMat;
      });
    valve("suction_valve", V(-3.1, 1.3, -0.8), "x", 0xd2a531);
    valve("discharge_valve", V(2.6, 2.9, 2.2), "x", 0xd2a531);
    valve("blowdown_valve", V(1.2, 4.0, 2.2), "y", 0xb43b2c);

    g("lube", (gr) => {
      const m = mat(0x7d8a6a, { metalness: 0.3 });
      gr.add(box(1.8, 0.8, 1.0, m, 2.6, 0.7, -2.1));
      const fm = mat(0x9aa3a8, { metalness: 0.5 });
      for (const x of [1.95, 2.35]) gr.add(cylBetween(V(x, 1.1, -1.75), V(x, 1.75, -1.75), 0.14, fm));
      this.dpFace = new THREE.MeshBasicMaterial({ map: textTexture(["dP"], { bg: "#f2f2f2", fg: "#222", w: 128, h: 128, size: 40 }) });
      const gauge = new THREE.Mesh(new THREE.CircleGeometry(0.16, 24), this.dpFace);
      gauge.position.set(2.95, 1.35, -1.6);
      gr.add(gauge);
      gr.add(box(0.08, 0.5, 0.08, fm, 2.95, 1.1, -1.65));
    });

    g("detector", (gr) => {
      const m = mat(0x707a80);
      gr.add(cylBetween(V(0.9, 0, 1.6), V(0.9, 2.2, 1.6), 0.04, m));
      gr.add(box(0.26, 0.2, 0.18, mat(0xd8b02a), 0.9, 2.25, 1.6));
      this.beaconMat = mat(0x7a2a24, { emissive: 0x000000, transparent: true, opacity: 0.95 });
      const beacon = new THREE.Mesh(new THREE.SphereGeometry(0.11, 16, 12), this.beaconMat);
      beacon.position.set(0.9, 2.47, 1.6);
      gr.add(beacon);
    });
    this.beaconLight = new THREE.PointLight(0xff2a1a, 0, 9);
    this.beaconLight.position.set(0.9, 2.6, 1.6);
    scene.add(this.beaconLight);

    g("panel", (gr) => {
      gr.add(box(1.15, 1.9, 0.45, mat(0x9aa1a6, { metalness: 0.3 }), -2.6, 0.95, 4.3));
      this.panelTex = textTexture(["LCP-101", "RUNNING"], { bg: "#14301f", fg: "#7dffb0", w: 256, h: 128, size: 36 });
      const screen = new THREE.Mesh(new THREE.PlaneGeometry(0.8, 0.4), new THREE.MeshBasicMaterial({ map: this.panelTex }));
      screen.position.set(-2.6, 1.6, 4.531);
      gr.add(screen);
      this.esdMat = mat(0xc4271f, { emissive: 0x000000 });
      const esd = cylBetween(V(-2.6, 1.15, 4.52), V(-2.6, 1.15, 4.66), 0.12, this.esdMat, 20);
      gr.add(esd);
      gr.add(cylBetween(V(-2.88, 0.85, 4.52), V(-2.88, 0.85, 4.6), 0.07, mat(0x2c8a4b), 16));
      gr.add(cylBetween(V(-2.32, 0.85, 4.52), V(-2.32, 0.85, 4.6), 0.07, mat(0x222222), 16));
    });
    this.lastPanelText = "";

    g("mcp", (gr) => {
      gr.add(cylBetween(V(-5.6, 0, 3.4), V(-5.6, 1.4, 3.4), 0.05, mat(0x707a80)));
      gr.add(box(0.3, 0.3, 0.12, mat(0xc4271f), -5.6, 1.5, 3.45));
    });

    g("windsock", (gr) => {
      gr.add(cylBetween(V(-6.8, 0, -3.6), V(-6.8, 5.6, -3.6), 0.05, mat(0x707a80)));
      const sock = new THREE.Mesh(new THREE.ConeGeometry(0.3, 1.5, 16, 1, true), mat(0xe5702a, { side: THREE.DoubleSide }));
      sock.rotation.z = Math.PI / 2; // tip points +x: the way the wind blows
      sock.position.set(-6.0, 5.45, -3.6);
      gr.add(sock);
      this.sock = sock;
    });

    const muster = (id, x, letter) =>
      g(id, (gr) => {
        gr.add(cylBetween(V(x - 0.6, 0, 6), V(x - 0.6, 1.9, 6), 0.04, mat(0x707a80)));
        gr.add(cylBetween(V(x + 0.6, 0, 6), V(x + 0.6, 1.9, 6), 0.04, mat(0x707a80)));
        const tex = textTexture(["MUSTER", "POINT " + letter], { bg: "#1f6b3a", fg: "#ffffff", w: 256, h: 160, size: 46 });
        const sign = new THREE.Mesh(new THREE.BoxGeometry(1.5, 0.95, 0.05), [
          mat(0x1f6b3a), mat(0x1f6b3a), mat(0x1f6b3a), mat(0x1f6b3a),
          new THREE.MeshBasicMaterial({ map: tex }), mat(0x1f6b3a),
        ]);
        sign.position.set(x, 1.7, 6);
        gr.add(sign);
      });
    muster("muster_a", -10, "A");
    muster("muster_b", 10, "B");

    // Effects
    this.gas = new Plume(scene, { count: 700, color: 0xeef4f8, size: 0.55, opacity: 0.32, life: 4.5 });
    this.flare = new Plume(scene, { count: 400, color: 0xdfe5e8, size: 0.8, opacity: 0.25, life: 4 });
    this.fire = new THREE.Group();
    this.fireMat = new THREE.MeshBasicMaterial({ color: 0xff7a1a, transparent: true, opacity: 0.8, depthWrite: false });
    for (let i = 0; i < 6; i++) {
      const s = new THREE.Mesh(new THREE.SphereGeometry(1, 20, 14), this.fireMat);
      s.position.set((Math.random() - 0.3) * 2.5, 2.2 + Math.random() * 1.5, 1 + (Math.random() - 0.5) * 2);
      this.fire.add(s);
    }
    this.fireLight = new THREE.PointLight(0xff8a2a, 0, 30);
    this.fireLight.position.copy(FLANGE_POS);
    this.fire.visible = false;
    scene.add(this.fire, this.fireLight);
    this.fireAge = 0;
  }

  register(id, group) {
    const meshes = [];
    group.traverse((c) => {
      if (c.isMesh) {
        c.userData.target = id;
        meshes.push(c);
        this.pickables.push(c);
      }
    });
    const bbox = new THREE.Box3().setFromObject(group);
    const anchor = V((bbox.min.x + bbox.max.x) / 2, bbox.max.y + 0.15, (bbox.min.z + bbox.max.z) / 2);
    const label = document.createElement("div");
    label.className = "label3d";
    label.innerHTML = `<span>${EQUIPMENT[id]?.tag ?? id}</span><small></small>`;
    this.labelLayer.appendChild(label);
    this.targets.set(id, { group, anchor, meshes, label, status: "", tone: "" });
  }

  applyTheme() {
    const css = getComputedStyle(document.documentElement);
    const sky = css.getPropertyValue("--scene-sky").trim() || "#cdd5da";
    const ground = css.getPropertyValue("--scene-ground").trim() || "#8f999e";
    this.scene.background = new THREE.Color(sky);
    this.scene.fog = new THREE.Fog(new THREE.Color(sky), 38, 95);
    this.groundMat.color.set(ground);
  }

  resetView() {
    const portrait = this.host.clientWidth < this.host.clientHeight;
    if (portrait) {
      this.camera.position.set(4, 15, 21);
      this.controls.target.set(-0.4, 1.2, 0.6);
    } else {
      this.camera.position.set(8.5, 7.5, 12.5);
      this.controls.target.set(-0.6, 1.6, 1.2);
    }
    this.controls.update();
  }

  resize() {
    const w = this.host.clientWidth || 1;
    const h = this.host.clientHeight || 1;
    this.renderer.setSize(w, h, false);
    this.camera.aspect = w / h;
    // Keep the skid's width in view on tall phone screens.
    this.camera.fov = w < h ? 58 : 45;
    this.camera.updateProjectionMatrix();
  }

  bindPicking() {
    const el = this.renderer.domElement;
    const ray = new THREE.Raycaster();
    let down = null;
    el.addEventListener("pointerdown", (e) => {
      down = { x: e.clientX, y: e.clientY, t: performance.now() };
    });
    el.addEventListener("pointerup", (e) => {
      if (!down) return;
      const moved = Math.hypot(e.clientX - down.x, e.clientY - down.y);
      const quick = performance.now() - down.t < 600;
      down = null;
      if (moved > 8 || !quick) return;
      const rect = el.getBoundingClientRect();
      const ndc = new THREE.Vector2(((e.clientX - rect.left) / rect.width) * 2 - 1, -((e.clientY - rect.top) / rect.height) * 2 + 1);
      ray.setFromCamera(ndc, this.camera);
      const hit = ray.intersectObjects(this.pickables, false)[0];
      this.onPick?.(hit ? hit.object.userData.target : null);
    });
  }

  select(id) {
    if (this.selected) {
      const prev = this.targets.get(this.selected);
      prev?.meshes.forEach((m) => m.material.emissive && m.material.emissive.setHex(m.userData.baseEmissive ?? 0));
      prev?.label.classList.remove("selected");
    }
    this.selected = id;
    const cur = id ? this.targets.get(id) : null;
    if (cur) {
      const accent = new THREE.Color(getComputedStyle(document.documentElement).getPropertyValue("--accent").trim() || "#1d5e8a");
      cur.meshes.forEach((m) => {
        if (!m.material.emissive) return;
        if (m.userData.baseEmissive === undefined) m.userData.baseEmissive = m.material.emissive.getHex();
        m.material.emissive.copy(accent).multiplyScalar(0.45);
      });
      cur.label.classList.add("selected");
    }
  }

  setLabel(id, status, tone = "") {
    const t = this.targets.get(id);
    if (!t) return;
    if (t.status !== status) {
      t.status = status;
      t.label.querySelector("small").textContent = status;
    }
    if (t.tone !== tone) {
      t.label.classList.remove("warn", "alarm");
      if (tone) t.label.classList.add(tone);
      t.tone = tone;
    }
  }

  reset() {
    this.gas.clear();
    this.flare.clear();
    this.fire.visible = false;
    this.fireLight.intensity = 0;
    this.fireAge = 0;
    this.select(null);
    this.compressorMats.forEach((m) => m.emissive.setHex(0));
    this.resetView();
  }

  // ---------------------------------------------------------------- per frame
  frame(dt, drill) {
    this.time += dt;
    const s = drill?.s;
    if (s) {
      // Gas escaping at the flange: a few wisps on the walkdown, a jet once the gasket blows.
      const intensity = s.leak * (s.P / 42);
      this.gas.update(dt, intensity * 140, FLANGE_POS, V(0, 0.4, 2.2 * intensity + 0.3), 0.9, 0.25);
      const frost = s.leak > 0 ? Math.min(1, 0.55 + intensity * 0.6) : 0;
      this.flangeMat.color.setRGB(0.6 + 0.35 * frost, 0.64 + 0.33 * frost, 0.66 + 0.34 * frost);

      // Blowdown to flare.
      this.flare.update(dt, s.bdv ? Math.max(0, s.P / 42) * 90 : 0, STACK_TIP, V(0, 4, 0), 0.8, 1.5);

      // Compressor motion: coupling turns with speed, casing shakes with vibration.
      if (this.coupling) this.coupling.rotateY(dt * (s.rpm / 9800) * 30);
      if (this.compressorGroup) {
        const shake = s.rpm > 200 && s.vib > 4 ? (s.vib - 3.5) * 0.004 : 0;
        this.compressorGroup.position.set((Math.random() - 0.5) * shake, (Math.random() - 0.5) * shake, 0);
      }

      // Alarm beacon.
      const flashing = s.alarm && !drill.ended;
      const on = flashing && Math.sin(this.time * 9) > 0;
      this.beaconMat.emissive.setHex(on ? 0xff2a1a : 0x000000);
      this.beaconLight.intensity = on ? 6 : 0;

      // Valve position indicators.
      const ind = (id, open) => this.targets.get(id).group.userData.indicator.color.setHex(open < 0.02 ? 0x2c8a4b : open > 0.98 ? 0xc4271f : 0xe08a00);
      ind("suction_valve", s.sdv101);
      ind("discharge_valve", s.sdv102);
      ind("blowdown_valve", s.bdv);

      this.esdMat.emissive.setHex(s.esd ? 0x661010 : 0x000000);
      const panelText = s.esd ? "ESD ACTIVE" : s.tripped ? "TRIPPED" : s.running ? "RUNNING" : "STOPPED";
      if (panelText !== this.lastPanelText) {
        this.lastPanelText = panelText;
        const bad = panelText !== "RUNNING";
        this.panelTex.redraw(["LCP-101", panelText], bad ? "#3a1210" : "#14301f", bad ? "#ff8f86" : "#7dffb0");
      }

      if (this.sock) this.sock.rotation.x = Math.sin(this.time * 2.3) * 0.08;

      // Failure effects.
      if (drill.failed && drill.scn.id === "gas_leak") {
        this.fire.visible = true;
        this.fireAge += dt;
        const k = Math.min(1, this.fireAge / 1.2);
        this.fire.children.forEach((m, i) => m.scale.setScalar(0.4 + k * (1.6 + i * 0.25)));
        this.fireMat.opacity = 0.85 * (1 - Math.min(1, this.fireAge / 6)) + 0.1;
        this.fireLight.intensity = 40 * (1 - Math.min(1, this.fireAge / 6));
      } else if (drill.failed) {
        this.compressorMats.forEach((m) => m.emissive.setHex(Math.sin(this.time * 4) > 0 ? 0x5a1505 : 0x2a0a02));
      }
    }

    this.controls.update();
    this.renderer.render(this.scene, this.camera);
    this.placeLabels();
  }

  placeLabels() {
    const w = this.host.clientWidth;
    const h = this.host.clientHeight;
    const p = new THREE.Vector3();
    for (const t of this.targets.values()) {
      p.copy(t.anchor).project(this.camera);
      const visible = p.z < 1 && Math.abs(p.x) < 1.05 && Math.abs(p.y) < 1.05;
      t.label.hidden = !visible;
      if (visible) {
        const x = (p.x * 0.5 + 0.5) * w;
        const y = (-p.y * 0.5 + 0.5) * h;
        t.label.style.transform = `translate(${x.toFixed(1)}px, ${y.toFixed(1)}px) translate(-50%, -100%)`;
      }
    }
  }
}
