"""Narrow accidental research-file guard, not a substitute for disclosure review."""
from pathlib import PurePosixPath
import subprocess


def violations(paths):
    restricted = {"research-data", "research-cache", "registered-scans", "validation-intake", "__pycache__"}
    binary_data = {".ply", ".obj", ".npy", ".npz", ".h5", ".hdf5", ".parquet", ".dcm", ".pyc"}
    return [name for name in paths if restricted.intersection(PurePosixPath(name).parts) or PurePosixPath(name).suffix.lower() in binary_data
            or name.endswith((".research-private.json", ".validation-package.zip"))]


if __name__ == "__main__":
    tracked = subprocess.check_output(["git", "ls-files", "-z"], text=True).split("\0")
    bad = violations(tracked)
    if bad:
        raise SystemExit("Restricted research paths staged/tracked: "+", ".join(bad))
    print("Research path guard passed; manual disclosure review remains required.")
