"""Regenerate every narration clip with Sarvam AI (bulbul:v3, speaker "shubh") from the SAME
localized strings the trainee sees on screen, so audio and text can't drift apart again.

English -> Assets/Audio/Narration/<id>.wav            (overwritten in place: .meta/GUIDs kept)
Hindi   -> Assets/Audio/Narration/hi/<id>__hi.wav
Santali -> no Sarvam TTS voice exists; NarrationPlayer falls back to the Hindi clips.

Usage: python gen_narration.py [--dry-run] [--only en|hi] [--ids id1,id2]"""
import io, json, os, re, sys, time, wave
import sarvam

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
LOC = os.path.join(ROOT, "Assets/Resources/Localization")
NARR = os.path.join(ROOT, "Assets/Audio/Narration")
SPEAKER, MODEL, RATE = "shubh", "bulbul:v3", 24000

LANGS = {
    "en": {"code": "en-IN", "folder": "", "suffix": ""},
    "hi": {"code": "hi-IN", "folder": "hi", "suffix": "__hi"},
}

# narration id -> localization key whose on-screen text it speaks (same key unless noted)
LINES = [
    # Fire Safety, in the order a trainee hears them
    ("prompt_scan_floor", None), ("prompt_place_wall_content", None), ("prompt_place_fire", None),
    ("prompt_scanning_floor", None), ("prompt_mount_extinguishers", None),
    ("scenario_trashcan", None), ("scenario_outlet", None), ("scenario_cabinet", None), ("scenario_furnace", None),
    ("prompt_activate_alarm", None), ("prompt_shut_gas", None),
    ("warning_alarm_not_activated", "alarm_not_activated_warning_line"),
    ("guide_class_a", None), ("guide_class_bc", None), ("guide_class_abc", None), ("guide_default", None),
    ("warning_wrong_extinguisher", "wrong_extinguisher_warning_line"),
    ("pass_pull_pin", None), ("pass_aim_base", None), ("pass_squeeze", None), ("pass_sweep", None),
    ("pass_keep_spraying", None), ("pass_fire_out", None),
    ("result_great_job", "header_great_job"), ("result_not_quite", "header_not_quite"),
    ("result_furnace_exploded", "header_furnace_exploded"),
    # Machine Training (conveyor)
    ("conveyor_prompt_place", None),
    ("machine_task_run_auto", None), ("machine_task_back_to_auto", None), ("machine_task_stop", None),
    ("machine_task_estop", None), ("machine_task_reset", None), ("machine_task_manual", None),
    ("machine_task_jog", None), ("machine_task_speed", None),
    ("conveyor_estopped", None), ("conveyor_tap_start", None), ("conveyor_tap_stop", None),
    ("conveyor_hold_jog", None), ("conveyor_release_jog", None),
    ("machine_result_pass", None), ("machine_result_fail", None),
]

KEEP_CAPS = {"A", "B", "C", "BC", "ABC", "AR", "QR"}

# Spoken rewording where the on-screen text reads fine but the voice doesn't (checked by
# transcribing the generated clip back with Sarvam speech-to-text).
SPEECH_OVERRIDES = {
    # "red-and-yellow E-stop" was heard as "red and yellowish top"
    ("en", "machine_task_estop"): "Tap the emergency stop switch. It's the red and yellow switch on the side of the control box.",
}

# Hindi voice: say the English control labels the way a Hindi speaker would, in Devanagari,
# instead of leaving Latin words for the hi-IN voice to guess at.
HI_WORDS = [
    (r"\bE-stopped\b", "ई-स्टॉप"), (r"\bE-stop\b", "ई-स्टॉप"), (r"\bstart\b", "स्टार्ट"), (r"\bstop\b", "स्टॉप"),
    (r"\bmanual\b", "मैनुअल"), (r"\bauto\b", "ऑटो"), (r"\bA B C\b", "ए बी सी"), (r"\bB C\b", "बी सी"),
    (r"\bB/C\b", "बी या सी"), (r"(?<=क्लास )A\b", "ए"),
]


def speech_text(text, lang):
    """On-screen text -> something a TTS voice reads naturally."""
    t = text
    t = re.sub(r"[⚠✓✗]", "", t)
    t = re.sub(r"\s*\((?:I|O)\)", "", t)          # button glyph labels "(I)" / "(O)"
    t = t.replace("MANUAL/AUTO", "MANUAL AUTO")
    t = t.replace("…", "।" if lang == "hi" else ".").replace(" — ", ", ").replace("—", ", ")
    t = re.sub(r"(?<![A-Za-z])ABC(?![A-Za-z])", "A B C", t)
    t = re.sub(r"(?<![A-Za-z/])BC(?![A-Za-z])", "B C", t)

    def fix_caps(m):
        w = m.group(0)
        if w in KEEP_CAPS:
            return w
        if w.startswith("E-"):
            return "E-" + w[2:].lower()
        return w.lower()
    # whole words of 2+ capitals (optionally hyphenated like E-STOP); leaves "A B C" and "C-rated" alone
    t = re.sub(r"(?<![A-Za-z])[A-Z]{2,}(?:-[A-Z]+)*(?![A-Za-z])|(?<![A-Za-z])E-[A-Z]+(?![A-Za-z])", fix_caps, t)
    if lang == "hi":
        for pattern, word in HI_WORDS:
            t = re.sub(pattern, word, t, flags=re.IGNORECASE)
        t = t.replace(".", "।") if re.search(r"[ऀ-ॿ]", t) else t
    t = re.sub(r"\s+", " ", t).strip()
    # sentence case for a line that started all-caps ("GREAT JOB!" -> "great job!" -> "Great job!")
    if t and t[0].islower():
        t = t[0].upper() + t[1:]
    if t and t[-1] not in ".!?।":
        t += "।" if lang == "hi" else "."
    return t


def main():
    dry = "--dry-run" in sys.argv
    only = sys.argv[sys.argv.index("--only") + 1] if "--only" in sys.argv else None
    ids = set(sys.argv[sys.argv.index("--ids") + 1].split(",")) if "--ids" in sys.argv else None
    tables = {l: json.load(io.open(os.path.join(LOC, l + ".json"), encoding="utf-8")) for l in LANGS}
    manifest_lines = []
    for nid, key in LINES:
        key = key or nid
        entry = {"id": nid, "key": key, "text": {}, "files": {}}
        for lang, cfg in LANGS.items():
            src = tables[lang].get(key)
            if src is None:
                raise SystemExit(f"missing {lang} text for {key}")
            spoken = SPEECH_OVERRIDES.get((lang, nid)) or speech_text(src, lang)
            rel = (cfg["folder"] + "/" if cfg["folder"] else "") + nid + cfg["suffix"] + ".wav"
            entry["text"][lang] = spoken
            entry["files"][lang] = rel
            if dry or (only and lang != only) or (ids and nid not in ids):
                print(f"{lang} {nid:28} {spoken}")
                continue
            path = os.path.join(NARR, rel)
            os.makedirs(os.path.dirname(path), exist_ok=True)
            audio = sarvam.tts(spoken, cfg["code"], speaker=SPEAKER, model=MODEL, sample_rate=RATE)
            with open(path, "wb") as f:
                f.write(audio)
            with wave.open(path) as w:
                secs = w.getnframes() / w.getframerate()
            print(f"{lang} {nid:28} {secs:5.2f}s  {spoken}")
            time.sleep(0.2)
        manifest_lines.append(entry)

    if dry:
        return
    manifest = {
        "model": MODEL, "speaker": SPEAKER, "speech_sample_rate": RATE,
        "languages": {l: {"language_code": c["code"], "folder": c["folder"] or ".", "clip_suffix": c["suffix"]} for l, c in LANGS.items()},
        "santali": "Sarvam TTS has no Santali (sat) voice; NarrationPlayer plays the Hindi clips for Santali (see santaliVoiceFallback).",
        "source": "Spoken text is generated from Assets/Resources/Localization/<lang>.json (the on-screen strings) via speech_text() in the dev-time generator.",
        "lines": manifest_lines,
    }
    io.open(os.path.join(NARR, "manifest.json"), "w", encoding="utf-8", newline="\n").write(
        json.dumps(manifest, ensure_ascii=False, indent=1) + "\n")
    print("manifest written:", len(manifest_lines), "lines")


if __name__ == "__main__":
    main()
