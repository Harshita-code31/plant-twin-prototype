"""Small Sarvam AI client used by the dev-time localization/narration scripts.
Reads SARVAM_API_KEY from the project's gitignored .env (never printed)."""
import json, time, urllib.request, urllib.error

import os
ENV = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".env")


def api_key():
    for line in open(ENV, encoding="utf-8"):
        line = line.strip()
        if line.startswith("SARVAM_API_KEY="):
            return line.split("=", 1)[1].strip().strip('"').strip("'")
    raise SystemExit("SARVAM_API_KEY not found in .env")


KEY = api_key()


def _post(url, payload, retries=4):
    body = json.dumps(payload).encode("utf-8")
    for attempt in range(retries):
        req = urllib.request.Request(url, data=body, method="POST", headers={
            "api-subscription-key": KEY, "Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(req, timeout=60) as r:
                return json.loads(r.read().decode("utf-8"))
        except urllib.error.HTTPError as e:
            msg = e.read().decode("utf-8", "replace")[:300]
            if e.code in (429, 500, 502, 503, 504) and attempt < retries - 1:
                time.sleep(2 * (attempt + 1))
                continue
            raise RuntimeError(f"HTTP {e.code}: {msg}")
        except urllib.error.URLError:
            if attempt < retries - 1:
                time.sleep(2 * (attempt + 1))
                continue
            raise


def translate(text, target="sat-IN", source="en-IN"):
    r = _post("https://api.sarvam.ai/translate", {
        "input": text, "source_language_code": source, "target_language_code": target,
        "model": "sarvam-translate:v1", "numerals_format": "international"})
    return r["translated_text"]


def tts(text, language_code, speaker="shubh", model="bulbul:v3", sample_rate=24000, pace=1.0):
    r = _post("https://api.sarvam.ai/text-to-speech", {
        "text": text, "target_language_code": language_code, "speaker": speaker, "model": model,
        "speech_sample_rate": sample_rate, "pace": pace, "output_audio_codec": "wav"})
    import base64
    return base64.b64decode("".join(r["audios"]))
