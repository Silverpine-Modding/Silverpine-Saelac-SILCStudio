"""Run with the Python environment containing requirements.txt. No game files bundled."""
from pathlib import Path
import subprocess
import sys

root = Path(__file__).resolve().parent
subprocess.run([sys.executable, '-m', 'PyInstaller', '--noconfirm', '--onefile',
    '--name', 'SilverpineAssetReader', '--distpath', str(root/'dist'),
    '--workpath', str(root/'build'), '--specpath', str(root),
    '--collect-all', 'UnityPy', '--collect-all', 'archspec',
    '--exclude-module', 'fmod_toolkit', '--exclude-module', 'pyfmodex',
    '--exclude-module', 'torch', '--exclude-module', 'torchvision',
    '--exclude-module', 'matplotlib', '--exclude-module', 'scipy',
    '--exclude-module', 'pandas', '--exclude-module', 'tkinter',
    str(root/'read_assets.py')], check=True)
