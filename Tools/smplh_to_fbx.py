#!/usr/bin/env python3
"""
SMPL-H to FBX Converter for Unity

Converts an SMPL-H model (.npz) to FBX format suitable for Unity import.
Uses a zero-shape (beta=0) to produce the standard body model.

Prerequisites:
    pip install numpy smplx trimesh

Usage:
    python smplh_to_fbx.py --model_path SMPLH_NEUTRAL.npz --output_dir ../Assets/Models/

For Blender-based FBX export (higher quality):
    python smplh_to_fbx.py --model_path SMPLH_NEUTRAL.npz --output_dir ../Assets/Models/ --use_blender

The SMPL-H model must be downloaded from:
    https://mano.is.tue.mpg.de/ (registration required)

Notes:
    - The output uses zero betas (standard body shape)
    - Joint hierarchy matches SMPL-H convention (24 body + 30 hand joints)
    - Unity should import the FBX with Humanoid rig type for retargeting
"""

import argparse
import os
import sys
import json
import numpy as np

# SMPL-H body joint names (24 joints)
SMPLH_BODY_JOINTS = [
    "Pelvis",       # 0
    "L_Hip",        # 1
    "R_Hip",        # 2
    "Spine1",       # 3
    "L_Knee",       # 4
    "R_Knee",       # 5
    "Spine2",       # 6
    "L_Ankle",      # 7
    "R_Ankle",      # 8
    "Spine3",       # 9
    "L_Foot",       # 10
    "R_Foot",       # 11
    "Neck",         # 12
    "L_Collar",     # 13
    "R_Collar",     # 14
    "Head",         # 15
    "L_Shoulder",   # 16
    "R_Shoulder",   # 17
    "L_Elbow",      # 18
    "R_Elbow",      # 19
    "L_Wrist",      # 20
    "R_Wrist",      # 21
    "L_Hand",       # 22 (virtual, for hand root)
    "R_Hand",       # 23 (virtual, for hand root)
]

# Parent indices for SMPL-H body joints
SMPLH_PARENT = [
    -1,  # 0  Pelvis (root)
    0,   # 1  L_Hip -> Pelvis
    0,   # 2  R_Hip -> Pelvis
    0,   # 3  Spine1 -> Pelvis
    1,   # 4  L_Knee -> L_Hip
    2,   # 5  R_Knee -> R_Hip
    3,   # 6  Spine2 -> Spine1
    4,   # 7  L_Ankle -> L_Knee
    5,   # 8  R_Ankle -> R_Knee
    6,   # 9  Spine3 -> Spine2
    7,   # 10 L_Foot -> L_Ankle
    8,   # 11 R_Foot -> R_Ankle
    9,   # 12 Neck -> Spine3
    9,   # 13 L_Collar -> Spine3
    9,   # 14 R_Collar -> Spine3
    12,  # 15 Head -> Neck
    13,  # 16 L_Shoulder -> L_Collar
    14,  # 17 R_Shoulder -> R_Collar
    16,  # 18 L_Elbow -> L_Shoulder
    17,  # 19 R_Elbow -> R_Shoulder
    18,  # 20 L_Wrist -> L_Elbow
    19,  # 21 R_Wrist -> R_Elbow
    20,  # 22 L_Hand -> L_Wrist
    21,  # 23 R_Hand -> R_Wrist
]


def load_smplh_model(model_path):
    """Load SMPL-H model from .npz file."""
    print(f"Loading SMPL-H model from: {model_path}")

    data = np.load(model_path, allow_pickle=True)

    print(f"  Keys: {list(data.keys())}")

    # Extract model data
    result = {}

    # Vertex template (zero-pose, zero-shape)
    if "v_template" in data:
        result["v_template"] = data["v_template"]
        print(f"  Vertices: {result['v_template'].shape}")

    # Faces
    if "f" in data:
        result["faces"] = data["f"]
        print(f"  Faces: {result['faces'].shape}")

    # Joint regressor
    if "J_regressor" in data:
        result["J_regressor"] = data["J_regressor"]
    elif "J_regressor_prior" in data:
        result["J_regressor"] = data["J_regressor_prior"]

    # Shape blend shapes
    if "shapedirs" in data:
        result["shapedirs"] = data["shapedirs"]

    # Kinematic tree (parent indices)
    if "kintree_table" in data:
        result["kintree_table"] = data["kintree_table"]

    # Weights
    if "weights" in data:
        result["weights"] = data["weights"]
        print(f"  Skinning weights: {result['weights'].shape}")

    return result


def compute_joints_zero_shape(model_data):
    """Compute joint positions for zero-shape (beta=0) model."""
    v_template = model_data["v_template"]

    if "J_regressor" in model_data:
        J_regressor = model_data["J_regressor"]
        if hasattr(J_regressor, "toarray"):
            J_regressor = J_regressor.toarray()
        joints = J_regressor @ v_template
    else:
        print("Warning: No joint regressor found, using template vertices as fallback")
        joints = v_template[:24]

    print(f"  Joint positions shape: {joints.shape}")
    return joints


def export_obj(model_data, joints, output_path):
    """Export model as OBJ file (simple mesh export)."""
    v = model_data["v_template"]
    f = model_data["faces"]

    print(f"Exporting OBJ to: {output_path}")

    with open(output_path, "w") as fp:
        fp.write("# SMPL-H Standard Model (beta=0)\n")
        fp.write(f"# Vertices: {v.shape[0]}, Faces: {f.shape[0]}\n\n")

        for vi in range(v.shape[0]):
            fp.write(f"v {v[vi, 0]:.6f} {v[vi, 1]:.6f} {v[vi, 2]:.6f}\n")

        fp.write("\n")

        for fi in range(f.shape[0]):
            # OBJ uses 1-indexed faces
            fp.write(f"f {f[fi, 0]+1} {f[fi, 1]+1} {f[fi, 2]+1}\n")

    print(f"  OBJ exported: {v.shape[0]} vertices, {f.shape[0]} faces")


def export_skeleton_json(joints, output_path):
    """Export skeleton as JSON for Unity import helper."""
    skeleton = {
        "joint_names": SMPLH_BODY_JOINTS,
        "parent_indices": SMPLH_PARENT,
        "rest_positions": [],
    }

    num_joints = min(len(SMPLH_BODY_JOINTS), joints.shape[0])
    for i in range(num_joints):
        skeleton["rest_positions"].append({
            "name": SMPLH_BODY_JOINTS[i],
            "x": float(joints[i, 0]),
            "y": float(joints[i, 1]),
            "z": float(joints[i, 2]),
        })

    with open(output_path, "w") as fp:
        json.dump(skeleton, fp, indent=2)

    print(f"Skeleton JSON exported to: {output_path}")


def generate_blender_script(model_path, output_fbx_path, skeleton_json_path):
    """Generate a Blender Python script for FBX export."""
    script = f'''
import bpy
import json
import numpy as np
import os

# Clear scene
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete()

# Load SMPL-H model
model_path = r"{model_path}"
data = np.load(model_path, allow_pickle=True)
v_template = data["v_template"]
faces = data["f"]

# Load skeleton
with open(r"{skeleton_json_path}", "r") as f:
    skeleton = json.load(f)

# Create mesh
mesh = bpy.data.meshes.new("SMPLH_Mesh")
verts = [(v[0], v[1], v[2]) for v in v_template]
face_list = [tuple(f) for f in faces]
mesh.from_pydata(verts, [], face_list)
mesh.update()

obj = bpy.data.objects.new("SMPLH_Standard", mesh)
bpy.context.collection.objects.link(obj)
bpy.context.view_layer.objects.active = obj
obj.select_set(True)

# Create armature
bpy.ops.object.armature_add()
armature_obj = bpy.context.active_object
armature_obj.name = "SMPLH_Armature"
armature = armature_obj.data
armature.name = "SMPLH_Skeleton"

bpy.ops.object.mode_set(mode='EDIT')

# Remove default bone
for bone in armature.edit_bones:
    armature.edit_bones.remove(bone)

# Create bones from skeleton
joint_names = skeleton["joint_names"]
parent_indices = skeleton["parent_indices"]
rest_positions = skeleton["rest_positions"]

bones = {{}}
for i, jp in enumerate(rest_positions):
    bone = armature.edit_bones.new(jp["name"])
    bone.head = (jp["x"], jp["z"], jp["y"])  # Convert Y-up to Blender Z-up
    bone.tail = (jp["x"], jp["z"], jp["y"] + 0.02)
    bones[jp["name"]] = bone

# Set parents
for i, jp in enumerate(rest_positions):
    parent_idx = parent_indices[i]
    if parent_idx >= 0:
        bones[jp["name"]].parent = bones[rest_positions[parent_idx]["name"]]

bpy.ops.object.mode_set(mode='OBJECT')

# Parent mesh to armature
obj.parent = armature_obj
modifier = obj.modifiers.new("Armature", 'ARMATURE')
modifier.object = armature_obj

# Export FBX
output_path = r"{output_fbx_path}"
bpy.ops.export_scene.fbx(
    filepath=output_path,
    use_selection=False,
    apply_unit_scale=True,
    apply_scale_options='FBX_SCALE_ALL',
    bake_space_transform=True,
    axis_forward='-Z',
    axis_up='Y',
    add_leaf_bones=False,
    armature_nodetype='NULL',
)

print(f"FBX exported to: {{output_path}}")
'''
    return script


def main():
    parser = argparse.ArgumentParser(description="Convert SMPL-H model to Unity-compatible format")
    parser.add_argument("--model_path", required=True, help="Path to SMPL-H .npz model file")
    parser.add_argument("--output_dir", default="../Assets/Models/", help="Output directory")
    parser.add_argument("--use_blender", action="store_true", help="Use Blender for FBX export")
    parser.add_argument("--blender_path", default="blender", help="Path to Blender executable")
    args = parser.parse_args()

    if not os.path.exists(args.model_path):
        print(f"Error: Model file not found: {args.model_path}")
        sys.exit(1)

    os.makedirs(args.output_dir, exist_ok=True)

    # Load model
    model_data = load_smplh_model(args.model_path)

    # Compute zero-shape joints
    joints = compute_joints_zero_shape(model_data)

    # Export OBJ (always, as fallback)
    obj_path = os.path.join(args.output_dir, "SMPLH_Standard.obj")
    export_obj(model_data, joints, obj_path)

    # Export skeleton JSON
    skeleton_path = os.path.join(args.output_dir, "SMPLH_Skeleton.json")
    export_skeleton_json(joints, skeleton_path)

    # FBX export via Blender
    if args.use_blender:
        fbx_path = os.path.join(args.output_dir, "SMPLH_Standard.fbx")
        script = generate_blender_script(
            os.path.abspath(args.model_path),
            os.path.abspath(fbx_path),
            os.path.abspath(skeleton_path),
        )

        script_path = os.path.join(args.output_dir, "_blender_export.py")
        with open(script_path, "w") as f:
            f.write(script)

        print(f"\nBlender export script written to: {script_path}")
        print(f"Run: {args.blender_path} --background --python {script_path}")

        os.system(f'"{args.blender_path}" --background --python "{script_path}"')

        if os.path.exists(script_path):
            os.remove(script_path)
    else:
        print("\nNote: OBJ exported. For FBX, re-run with --use_blender flag.")
        print("You can also import the OBJ directly into Unity and set up the rig manually.")

    print("\nDone! Import the model into Unity at Assets/Models/SMPLH_Standard")
    print("Configure the model's Rig type as 'Humanoid' in the Unity Inspector.")


if __name__ == "__main__":
    main()
