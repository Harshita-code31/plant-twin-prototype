# Localization & narration tools (dev-time only, not shipped)

All scripts read `SARVAM_API_KEY` from the repo's gitignored `.env`.

| Script | What it does |
|---|---|
| `gen_narration.py` | Regenerates every narration clip with Sarvam TTS (`bulbul:v3`, speaker `shubh`) from the **on-screen strings** in `Assets/Resources/Localization/{en,hi}.json`, and rewrites `Assets/Audio/Narration/manifest.json`. English -> `Narration/<id>.wav` (overwritten in place, GUIDs kept), Hindi -> `Narration/hi/<id>__hi.wav`. `--dry-run` prints the spoken text only; `--only en|hi` limits to one language. New lines go in its `LINES` list, then drag the new clips into the scene's `NarrationPlayer`. |
| `translate_missing_sat.py` | Machine-translates every key missing from `sat.json` (Sarvam `sarvam-translate:v1`, Ol Chiki) and appends the new lines to `sat_machine_translated_review.csv`. |
| `sat_machine_translated_review.csv` | Every Santali line written by machine translation (plus two hand-made lines whose colour words were standardized) -- **needs a native Santali speaker's review**. |

Santali has **no Sarvam text-to-speech voice**; `NarrationPlayer` plays the Hindi clips when Santali is selected (`santaliVoiceFallback`).
