"""Experimentos sequenciais em mídia sintética; não recebe acervos do usuário.

Python 3.11+, FFmpeg/FFprobe e Untrunc externos. Cada execução exige pasta nova.
Logs completos e mídia ficam na pasta de trabalho; o JSON resume as evidências.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import platform
from pathlib import Path
import struct
import subprocess
import time
from datetime import datetime, timezone


def sha(path: Path) -> str:
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("ffmpeg", "ffprobe", "untrunc"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--work", type=Path, required=True)
    args = parser.parse_args()
    tools = {k: getattr(args, k).resolve(strict=True) for k in ("ffmpeg", "ffprobe", "untrunc")}
    work = args.work.resolve()
    work.mkdir(parents=True, exist_ok=False)
    logs = work / "logs"
    logs.mkdir()
    commands, checks, media, repairs = [], [], [], []

    def run(tool, parameters, timeout=60):
        cmd = [str(tools[tool]), *map(str, parameters)]
        number = len(commands)
        stdout_path, stderr_path = logs / f"{number:04d}.out", logs / f"{number:04d}.err"
        started = time.monotonic()
        # Arquivos de log evitam manter saídas ilimitadas de subprocessos na RAM.
        with stdout_path.open("wb") as out, stderr_path.open("wb") as err:
            process = subprocess.Popen(cmd, stdout=out, stderr=err, stdin=subprocess.DEVNULL,
                                       cwd=work, shell=False)
            timed_out = False
            try:
                code = process.wait(timeout=timeout)
            except subprocess.TimeoutExpired:
                timed_out = True
                process.kill()
                code = process.wait()
        record = {"id": number, "tool": tool, "argv": cmd, "exit_code": code,
                  "timeout": timed_out, "seconds": round(time.monotonic() - started, 3),
                  "stdout": str(stdout_path.relative_to(work)),
                  "stderr": str(stderr_path.relative_to(work)),
                  "stderr_bytes": stderr_path.stat().st_size}
        commands.append(record)
        return record, stdout_path, stderr_path

    def check(name, condition, detail=None):
        checks.append({"name": name, "passed": bool(condition), "detail": detail})

    def probe(path):
        record, out, _ = run("ffprobe", ["-v", "error", "-show_error", "-show_format",
                                       "-show_streams", "-of", "json", path])
        try:
            data = json.loads(out.read_text(encoding="utf-8", errors="replace"))
        except json.JSONDecodeError:
            data = {}
        return record, data

    def decode(path, index, start=None):
        options = ["-hide_banner", "-nostdin", "-v", "warning", "-threads", "1"]
        if start is not None:
            options += ["-ss", str(start)]
        options += ["-i", path, "-map", f"0:{index}"]
        if start is not None:
            options += ["-t", "1"]
        # Hashes de frames/samples decodificados permitem comparar conteúdo sem timestamps.
        options += ["-threads", "1", "-f", "framehash", "-hash", "sha256", "-"]
        record, out, err = run("ffmpeg", options)
        digest = hashlib.sha256()
        count, first, last = 0, None, None
        with out.open(encoding="utf-8", errors="replace") as content:
            for line in content:
                if line.startswith("#") or not line.strip():
                    continue
                fields = [value.strip() for value in line.split(",")]
                if len(fields) == 6:
                    count += 1
                    first = first or fields[2]
                    last = fields[2]
                    digest.update((fields[5] + "\n").encode())
        return {"command": record["id"], "exit_code": record["exit_code"], "units": count,
                "decoded_sha256": digest.hexdigest(), "first_pts": first, "last_pts": last,
                "diagnostics": err.stat().st_size > 0}

    def inspect(path, samples=False):
        record, data = probe(path)
        entry = {"file": path.name, "bytes": path.stat().st_size, "sha256": sha(path),
                 "probe_command": record["id"], "probe_exit": record["exit_code"],
                 "format": data.get("format", {}).get("format_name"), "streams": []}
        for stream in data.get("streams", []):
            if stream.get("codec_type") not in ("audio", "video"):
                continue
            index = stream["index"]
            result = {"index": index, "type": stream["codec_type"], "codec": stream.get("codec_name"),
                      "duration": stream.get("duration"), "full": decode(path, index)}
            if samples:
                result["samples"] = [{"requested_seconds": pos, **decode(path, index, pos)}
                                     for pos in (0, 3.5, 7)]
            entry["streams"].append(result)
        media.append(entry)
        return entry

    def generate(name, video, audio, extra=()):
        path = work / name
        options = ["-hide_banner", "-nostdin", "-v", "error", "-n", "-f", "lavfi", "-i",
                   "testsrc2=size=160x96:rate=10:duration=8"]
        if audio:
            options += ["-f", "lavfi", "-i", "sine=frequency=1000:sample_rate=48000:duration=8"]
        options += ["-map", "0:v:0", "-c:v", video, "-threads", "1", "-g", "10"]
        if audio:
            options += ["-map", "1:a:0", "-c:a", audio]
        options += [*extra, path]
        record, _, _ = run("ffmpeg", options)
        if record["exit_code"] != 0:
            check("gerar:" + name, False, record["id"])
            return None
        check("gerar:" + name, True, record["id"])
        return path

    # Parser mínimo apenas para adulterar fixtures ISO BMFF geradas nesta execução.
    def boxes(blob):
        offset = 0
        while offset + 8 <= len(blob):
            size, kind = struct.unpack_from(">I4s", blob, offset)
            header = 8
            if size == 1:
                size = struct.unpack_from(">Q", blob, offset + 8)[0]
                header = 16
            if size == 0:
                size = len(blob) - offset
            if size < header or offset + size > len(blob):
                raise ValueError("Fixture ISO BMFF inválida")
            yield offset, size, kind, header
            offset += size

    try:
        versions = {}
        for name in tools:
            record, out, err = run(name, ["-version"] if name != "untrunc" else ["-h"])
            versions[name] = {"binary_sha256": sha(tools[name]),
                              "output": (out.read_text(errors="replace") + err.read_text(errors="replace"))[:18000]}
        for option in ("-demuxers", "-decoders", "-encoders", "-buildconf", "-L"):
            record, _, _ = run("ffmpeg", ["-hide_banner", option])
            versions[option] = record["id"]

        specifications = [
            ("av.mp4", "libx264", "aac", ()),
            ("camera.mov", "prores_ks", "pcm_s16le", ("-profile:v", "0")),
            ("av.avi", "mpeg4", "libmp3lame", ()),
            ("av.mkv", "libx264", "aac", ()),
            ("av.ts", "mpeg2video", "mp2", ()),
            ("av.webm", "libvpx-vp9", "libopus", ("-deadline", "realtime")),
            ("av.mpg", "mpeg2video", "mp2", ()),
            ("av.flv", "flv", "libmp3lame", ("-ar", "44100")),
            ("av.wmv", "wmv2", "wmav2", ()),
            ("hevc.mp4", "libx265", "aac", ("-preset", "ultrafast", "-x265-params", "pools=1:frame-threads=1")),
            ("video_only.mp4", "libx264", None, ()),
            ("reference.mp4", "libx264", "aac", ("-vf", "hue=h=90")),
        ]
        healthy = {}
        for name, video, audio, extra in specifications:
            path = generate(name, video, audio, extra)
            if path is None:
                continue
            original_hash = sha(path)
            result = inspect(path, samples=True)
            healthy[name] = result
            check("modos:" + name, result["probe_exit"] == 0 and all(
                x["full"]["exit_code"] == 0 and x["full"]["units"] > 0 and
                all(y["exit_code"] == 0 and y["units"] > 0 for y in x["samples"])
                for x in result["streams"]) and len(result["streams"]) == (2 if audio else 1))
            check("original_preservado:" + name, original_hash == sha(path))

        baseline = work / "av.mp4"
        payload = baseline.read_bytes()
        table = list(boxes(payload))
        moov = next(item for item in table if item[2] == b"moov")
        missing = work / "missing_moov.mp4"
        missing.write_bytes(payload[:moov[0]] + payload[moov[0] + moov[1]:])
        missing_result = inspect(missing)
        check("moov_ausente_detectado", missing_result["probe_exit"] != 0)

        mdat = next(item for item in table if item[2] == b"mdat")
        damaged = bytearray(payload)
        pos = mdat[0] + mdat[3] + (mdat[1] - mdat[3]) // 2
        damaged[pos:pos + 2048] = b"\0" * min(2048, len(damaged) - pos)
        damaged_path = work / "payload_damaged.mp4"
        damaged_path.write_bytes(damaged)
        damaged_result = inspect(damaged_path)
        check("payload_danificado_com_estrutura_legivel", damaged_result["probe_exit"] == 0)
        check("decoder_identifica_dano", any(s["full"]["diagnostics"] or s["full"]["exit_code"] != 0
                                            for s in damaged_result["streams"]))

        # Truncamento de transporte; recuperável somente o conteúdo ainda existente.
        ts = work / "av.ts"
        truncated = work / "truncated.ts"
        truncated.write_bytes(ts.read_bytes()[:ts.stat().st_size * 2 // 3])
        inspect(truncated)

        # AppleDouble sintético de 4 KiB: assinatura não deve ser confundida com mídia.
        sidecar = work / "._av.mp4"
        sidecar.write_bytes(struct.pack(">II", 0x00051607, 0x00020000) + bytes(4088))
        sidecar_result = inspect(sidecar)
        check("appledouble_nao_video", sidecar_result["probe_exit"] != 0 and not sidecar_result["streams"])

        renamed = work / "signature.bin"
        renamed.write_bytes(payload)
        renamed_result = inspect(renamed)
        check("identifica_sem_extensao", renamed_result["probe_exit"] == 0 and len(renamed_result["streams"]) == 2)

        for filename in ("av.mp4", "missing_moov.mp4", "truncated.ts", "payload_damaged.mp4"):
            source = work / filename
            before = sha(source)
            candidate = work / (source.stem + "_remux.mkv")
            record, _, _ = run("ffmpeg", ["-hide_banner", "-nostdin", "-v", "warning", "-n",
                                         "-i", source, "-map", "0:v?", "-map", "0:a?", "-c", "copy", candidate])
            result = inspect(candidate) if candidate.exists() and candidate.stat().st_size else None
            repairs.append({"tool": "ffmpeg", "source": filename, "command": record["id"],
                            "exit_code": record["exit_code"], "candidate": result["file"] if result else None})
            check("remux_preserva_original:" + filename, before == sha(source))
            if filename == "missing_moov.mp4":
                check("remux_nao_reconstroi_moov", record["exit_code"] != 0)
            if filename == "av.mp4" and result:
                check("remux_preserva_conteudo", [s["full"]["decoded_sha256"] for s in result["streams"]] ==
                      [s["full"]["decoded_sha256"] for s in healthy["av.mp4"]["streams"]])

        original_hash = sha(missing)
        record, _, _ = run("untrunc", [work / "reference.mp4", missing])
        candidates = list(work.glob("missing_moov*_fixed*.mp4"))
        recovered = inspect(candidates[0]) if candidates else None
        repairs.append({"tool": "untrunc", "source": missing.name, "command": record["id"],
                        "exit_code": record["exit_code"], "candidate": recovered["file"] if recovered else None})
        check("untrunc_preserva_original", original_hash == sha(missing))
        check("untrunc_recupera_moov_sintetico", recovered is not None and recovered["probe_exit"] == 0 and
              len(recovered["streams"]) == 2 and all(s["full"]["exit_code"] == 0 and s["full"]["units"] > 0
                                                   for s in recovered["streams"]))
        if recovered:
            check("untrunc_preserva_conteudo", [s["full"]["decoded_sha256"] for s in recovered["streams"]] ==
                  [s["full"]["decoded_sha256"] for s in healthy["av.mp4"]["streams"]])
    finally:
        report = {"schema": 1, "utc": datetime.now(timezone.utc).isoformat(), "platform": platform.platform(),
                  "python": platform.python_version(), "scope": "synthetic_only_not_product_acceptance",
                  "tools": locals().get("versions", {}), "checks": checks, "media": media,
                  "repairs": repairs, "commands": commands,
                  "passed": sum(c["passed"] for c in checks), "total": len(checks)}
        (work / "evidencias.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"passed": report["passed"], "total": report["total"],
                      "failed": [c["name"] for c in checks if not c["passed"]], "report": str(work / "evidencias.json")}))
    return 0 if all(c["passed"] for c in checks) else 1


if __name__ == "__main__":
    raise SystemExit(main())
