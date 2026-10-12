"""Fill every key that exists in en.json but not in sat.json with a Sarvam machine translation
(sarvam-translate:v1, en-IN -> sat-IN, Ol Chiki script), then list them in
sat_machine_translated_review.csv next to this script for a native Santali speaker to check.

Safeguards learned the hard way:
  * Labels printed on the equipment / buttons (START (I), STOP (O), E-STOP, MANUAL/AUTO, ABC...)
    are swapped for placeholder tokens before translating so they stay in Latin exactly as shown.
  * Every {n} placeholder must survive; a mangled token (e.g. "{90]") rejects the line.
  * The model writes U+1C7A where English has a colon -> replaced with ":" like the hand-made lines.
  * A trailing Ol Chiki full stop is dropped when the English line has no end punctuation
    (button captions, answer options).
  * Extinguisher colours are forced to one word each (the training is built on them).

Usage (from the repo root): python Tools/Localization/translate_missing_sat.py
Needs SARVAM_API_KEY in the repo's .env. Existing sat.json lines are never modified.
Afterwards, add any new Ol Chiki characters to Assets/Fonts/NotoSansOlChiki SDF.asset (static atlas).
"""
import csv, io, json, os, re, time
import sarvam

HERE = os.path.dirname(os.path.abspath(__file__))
LOC = os.path.join(HERE, "..", "..", "Assets", "Resources", "Localization")
REVIEW = os.path.join(HERE, "sat_machine_translated_review.csv")

PROTECTED = ["MANUAL/AUTO", "START (I)", "STOP (O)", "E-STOPPED", "E-STOP", "MANUAL", "AUTO",
             "START", "STOP", "RETRY", "PASS", "ABC", "BC", "QR"]
COLOURS = [  # (any of these) -> canonical Santali colour term
    (["ᱨᱤᱢᱤᱞ ᱨᱚᱝ", "ᱥᱮᱛᱮᱫ ᱨᱚᱝ", "ᱫᱷᱩᱲᱤ ᱨᱚᱝ"], "ᱥᱮᱛᱟᱜ ᱨᱚᱝ"),   # grey
    (["ᱦᱚᱲᱚᱝ ᱨᱚᱝ"], "ᱥᱟᱥᱟᱝ ᱨᱚᱝ"),                                  # yellow
    (["ᱟᱨᱟᱜᱽ ᱨᱚᱝ"], "ᱟᱨᱟᱜ ᱨᱚᱝ"),                                   # red
]


def protect(text):
    tokens, n = {}, 90
    for term in PROTECTED:
        pattern = re.compile(r"(?<![A-Za-z])" + re.escape(term) + r"(?![A-Za-z])")
        while (m := pattern.search(text)):
            tok = "{%d}" % n
            tokens[tok] = term
            text = text[:m.start()] + tok + text[m.end():]
            n += 1
    for m in reversed(list(re.finditer(r"(?<=Class )A(?![A-Za-z])", text))):
        tok = "{%d}" % n
        tokens[tok] = "A"
        text = text[:m.start()] + tok + text[m.end():]
        n += 1
    return text, tokens


def postprocess(src, out, tokens):
    for tok, term in tokens.items():
        out = out.replace(tok, term)
    if ":" in src:
        out = re.sub("ᱺ(?=\\s|$)", ":", out)
    if src.rstrip()[-1:] not in ".!?…":
        out = re.sub(r"\s*᱾\s*$", "", out)
    for variants, canonical in COLOURS:
        for v in variants:
            out = out.replace(v, canonical)
    return out


def main():
    en = json.load(io.open(os.path.join(LOC, "en.json"), encoding="utf-8"))
    hi = json.load(io.open(os.path.join(LOC, "hi.json"), encoding="utf-8"))
    sat_path = os.path.join(LOC, "sat.json")
    sat_text = io.open(sat_path, encoding="utf-8").read()
    sat = json.loads(sat_text)

    results, problems = {}, []
    for k in (k for k in en if k not in sat):
        src = en[k]
        if k.startswith("quiz_opt_pass_wrong") or hi.get(k) == src:
            results[k] = src  # intentionally English (made-up acronyms, codes)
            continue
        prot, tokens = protect(src)
        try:
            out = postprocess(src, sarvam.translate(prot), tokens)
        except Exception as e:
            problems.append((k, str(e)))
            continue
        if k == "quiz_opt_pass_correct":
            out = src + " (" + out + ")"
        if (set(re.findall(r"\{\d+\}", src)) != set(re.findall(r"\{\d+\}", out))
                or re.search(r"[\{\[]9\d[\}\]]", out)):
            problems.append((k, f"placeholder mismatch: {out!r}"))
            continue
        results[k] = out
        print(f"{k}: {out}")
        time.sleep(0.15)

    if results:
        body = sat_text.rstrip()[:-1].rstrip()
        out = body + ",\n\n" + ",\n".join("  %s: %s" % (json.dumps(k, ensure_ascii=False), json.dumps(v, ensure_ascii=False))
                                          for k, v in results.items()) + "\n}\n"
        json.loads(out)
        io.open(sat_path, "w", encoding="utf-8", newline="\n").write(out)
        new_file = not os.path.exists(REVIEW)
        with io.open(REVIEW, "a", encoding="utf-8-sig", newline="") as f:
            w = csv.writer(f)
            if new_file:
                w.writerow(["key", "english", "hindi", "santali_machine_translation", "reviewed_ok", "correction"])
            for k, v in results.items():
                w.writerow([k, en[k], hi.get(k, ""), v, "", ""])
    print(f"\nadded {len(results)} lines, {len(problems)} problems")
    for p in problems:
        print("PROBLEM", p)


if __name__ == "__main__":
    main()
