"""Offline structural reproduction; restricted benchmarks stay gated and local."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import socket
import signal
import subprocess
import sys
import time
import urllib.request


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--core-only", action="store_true", help="Explicitly omit browser checks; report is partial")
    parser.add_argument("--deltashape-config", type=Path, help="Private configuration for gated real-data evaluation; report remains in its restricted output path")
    parser.add_argument("--deltashape-output", type=Path, help="Approved restricted output file outside both repository and structural report")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    out = args.output.resolve()
    if out.exists() and any(out.iterdir()):
        raise ValueError("Choose a fresh output directory; preserve earlier evidence")
    out.mkdir(parents=True, exist_ok=True)
    env = dict(os.environ)
    env["PYTHONPATH"] = str(root/"tools")+os.pathsep+env.get("PYTHONPATH", "")
    env.update(MODEL_OUTPUT=str(out),GEOMETRY_FIXTURES=str(out/"geometry-fixtures.json"),GEOMETRY_OUTPUT=str(out/"geometry"),
               ANATOMY_OUTPUT=str(out/"anatomical"),AVATAR_OUTPUT=str(out/"avatar"),HYPOTHESIS_OUTPUT=str(out/"hypothesis"),CHECKIN_OUTPUT=str(out/"checkin"))
    flags = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0
    results = []

    def run(name, command, log_path=None):
        at = time.monotonic()
        with (log_path or out/(name+".log")).open("w", encoding="utf-8") as log:
            result = subprocess.run(command,cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT,creationflags=flags)
        results.append(dict(name=name,exit_code=result.returncode,seconds=time.monotonic()-at))
        print(name, "PASS" if result.returncode == 0 else "FAIL", flush=True)
        if result.returncode: raise RuntimeError(name+" failed; inspect its local log")

    def dotnet(project, *arguments):
        return ["dotnet","run","-c","Release","--no-build","--project","tools/WorkoutCalculator."+project,"--",*map(str,arguments)]

    server = None; server_log = None
    status = {"version":"model-reproduction-1","started_utc":datetime.now(timezone.utc).isoformat(),"evidence":"STRUCTURAL_BENCHMARKS_NOT_REAL_USER_ACCURACY",
              "deltashape":"BLOCKED_PENDING_DATA_ACCESS","pseudo_dxa":"BLOCKED_PREREQUISITE_AND_RIGHTS","renderer":"DEFERRED",
              "browser":"OMITTED_CORE_ONLY" if args.core_only else "PENDING","complete":False,"requested_checks_complete":False,"checks":results}
    try:
        status["source_commit"] = subprocess.check_output(["git","rev-parse","HEAD"],cwd=root,text=True,creationflags=flags).strip()
        status["source_dirty"] = bool(subprocess.check_output(["git","status","--porcelain"],cwd=root,text=True,creationflags=flags).strip())
        run("release-build",["dotnet","build","WorkoutCalculator.sln","-c","Release"])
        run("research-files",[sys.executable,"tools/research_repository_guard.py"])
        run("hall-legacy",dotnet("CompositionBenchmark","--legacy-only","--check"))
        run("hall-v3",dotnet("CompositionBenchmark","--check"))
        run("anatomical",dotnet("AnatomicalMorphBenchmark",root,out/"anatomical"))
        run("geometry-fixtures",dotnet("GeometryWarpBenchmark",root,out/"geometry-fixtures.json"))
        run("performance",dotnet("ShapeResearch","--performance",root,out/"performance.json"))
        run("prospective-empty",[sys.executable,"tools/validation_analysis.py","docs/data-access/prospective-intake.example.json",str(out/"prospective-empty")])
        if args.deltashape_config:
            # Config and raw data paths are never copied into the distributable structural report.
            # The research CLI checks DUA/storage/split/synthetic guards before reading meshes.
            if args.deltashape_output is None:
                raise ValueError("Explicit restricted DeltaShape output required")
            private_output = args.deltashape_output.resolve()
            if private_output.is_relative_to(root) or private_output.is_relative_to(out):
                raise ValueError("Restricted evaluation output must not enter repository or structural report")
            run("restricted-deltashape",[sys.executable,"-m","shape_research","evaluate",str(args.deltashape_config.resolve()),str(private_output)],private_output.with_suffix(".log"))
            status["deltashape"] = "PRIVATE_HELD_OUT_REVIEW_REQUIRED_NOT_PRODUCTION_GO"
        if not args.core_only:
            with socket.socket() as sock:
                sock.bind(("127.0.0.1",0));port = sock.getsockname()[1]
            env["APP_URL"] = "http://127.0.0.1:"+str(port)
            server_log = (out/"web.log").open("w",encoding="utf-8")
            server = subprocess.Popen(["dotnet","run","--project","src/WorkoutCalculator.Web","-c","Release","--no-build","--urls",env["APP_URL"]],cwd=root,env=env,stdout=server_log,stderr=subprocess.STDOUT,creationflags=flags,start_new_session=os.name != "nt")
            ready = False
            for _ in range(90):
                if server.poll() is not None: break
                try:
                    with urllib.request.urlopen(env["APP_URL"],timeout=1) as response: ready = response.status == 200
                except OSError: pass
                if ready: break
                time.sleep(1)
            if not ready: raise RuntimeError("Local app failed to start; inspect web.log")
            for script in ["model-startup-benchmark","avatar-creation-smoke","hypothesis-smoke","checkin-smoke","anatomical-muscle-audit","geometry-warp-benchmark","geometry-warp-webgl","geometry-warp-smoke"]:
                run(script,["node","tests/browser/"+script+".cjs"])
            status["browser"] = "PASS_DESKTOP_ONLY"
        status["requested_checks_complete"] = True
        status["complete"] = not args.core_only
    finally:
        if server is not None and server.poll() is None:
            # Terminate only the process tree launched by this tool; no name-based process killing.
            if os.name == "nt": subprocess.run(["taskkill","/PID",str(server.pid),"/T","/F"],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,creationflags=flags)
            else: os.killpg(server.pid,signal.SIGTERM)
            try: server.wait(timeout=10)
            except subprocess.TimeoutExpired: server.kill()
        if server_log: server_log.close()
        status["files"] = [{"path":str(p.relative_to(out)),"sha256":hashlib.sha256(p.read_bytes()).hexdigest()} for p in sorted(out.rglob("*.json")) if p.name != "manifest.json"]
        (out/"manifest.json").write_text(json.dumps(status,indent=2),encoding="utf-8")


if __name__ == "__main__":
    main()
