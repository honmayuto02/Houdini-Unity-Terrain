"""Headless export with hython.
usage: hython run_export.py <scene.hip> <node path> <out_dir> <name>
"""
import sys
import hou
import hf_export

hip, node_path, out_dir, name = sys.argv[1:5]
hou.hipFile.load(hip, suppress_save_prompt=True, ignore_load_warnings=True)
base, meta = hf_export.export(hou.node(node_path), out_dir, name)
print('exported', base, meta)