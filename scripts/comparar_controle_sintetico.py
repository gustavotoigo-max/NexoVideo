"""Investiga divergências da execução sintética, preservando suas evidências.

Uso: python scripts/comparar_controle_sintetico.py --work .research/run-01
Não é um comparador geral de arquivos: exige o manifesto da pesquisa sintética.
"""
import argparse
import collections
import json
from pathlib import Path
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--work", type=Path, required=True)
    args = parser.parse_args()
    work = args.work.resolve(strict=True)
    manifest = json.loads((work / "evidencias.json").read_text(encoding="utf-8"))
    if manifest.get("scope") != "synthetic_only_not_product_acceptance":
        raise ValueError("A pasta não contém uma execução sintética reconhecida")
    executable = manifest["commands"][0]["argv"][0]
    records = []

    def execute(name, parameters):
        command = [executable, "-hide_banner", "-nostdin", "-v", "warning", *map(str, parameters)]
        # Estes controles têm apenas oito segundos; não usar PIPE para vídeos arbitrários.
        process = subprocess.run(command, capture_output=True, timeout=40,
                                 stdin=subprocess.DEVNULL, shell=False)
        records.append({"name": name, "argv": command, "exit_code": process.returncode,
                        "stderr": process.stderr.decode(errors="replace")})
        return process.stdout

    frames = {}
    names = ("av.mp4", "missing_moov.mp4_fixed.mp4")
    for name in names:
        output = execute(name, ["-threads", "1", "-i", work / name, "-map", "0:v:0",
                                "-fps_mode", "passthrough", "-threads", "1", "-f", "framehash", "-"])
        frames[name] = [line.split(b",")[-1].strip().decode() for line in output.splitlines()
                        if line and not line.startswith(b"#")]
    audio = {name: execute("pcm:" + name, ["-threads", "1", "-i", work / name,
                                           "-map", "0:a:0", "-f", "s16le", "-"]) for name in names}
    original, candidate = (frames[name] for name in names)
    before, after = (audio[name] for name in names)
    flv = execute("flv_inicio_sem_seek", ["-threads", "1", "-i", work / "av.flv", "-t", "1",
                                        "-map", "0:v:0", "-map", "0:a:0", "-fps_mode:v", "passthrough",
                                        "-f", "framehash", "-"])
    flv_units = collections.Counter(line.split(b",")[0].strip().decode() for line in flv.splitlines()
                                    if line and not line.startswith(b"#"))
    report = {"video_frames": [len(original), len(candidate)],
              "video_order_equal": original == candidate,
              "video_multiset_equal": collections.Counter(original) == collections.Counter(candidate),
              "audio_bytes": [len(before), len(after)],
              "audio_equal_after_removing_1024_priming_samples": before == after[2048:],
              "audio_equal": before == after, "flv_initial_units_by_output_stream": dict(flv_units),
              "commands": records}
    target = work / "comparacao_detalhada.json"
    target.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({key: value for key, value in report.items() if key != "commands"}))
    return 0 if all(item["exit_code"] == 0 for item in records) else 1


if __name__ == "__main__":
    raise SystemExit(main())
