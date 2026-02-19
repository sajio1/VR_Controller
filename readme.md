
## **1. 核心技术栈与 SDK 引用**

该项目基于 **Unity 6 (URP)** 开发，深度集成了以下 SDK：

* **Meta XR SDK (v60+):** 用于高精度双手追踪（Hand Tracking）、控制器输入（Controller Input）以及相机位姿管理。
* **System.Net.WebSockets:** 用于与机器人端的 `rosbridge_server` 进行低延迟异步网络通信。
* **Meta Interaction SDK:** 用于通过 `PointableCanvas` 实现 UI 面板的手势戳（Poke）和射线交互。

---

## **2. 脚本模块与函数详解**

### **A. 输入中枢：`HandTrackingController.cs`**

**功能：** 提供统一的输入抽象层，支持手势和手柄的无缝切换。

* **关键函数：**
  * `UpdateRightHandMode()` / `UpdateLeftHandMode()`: 核心更新逻辑。通过 `OVRHand.IsDataHighConfidence` 确保控制数据的可靠性。
  * `GetFingerPinchStrength()`: 获取食指（Gripper）和中指（Clutch）的捏合强度，将连续的物理动作转化为 0.0-1.0 的控制数值。
  * `FindWristBone()`: 遍历 `OVRSkeleton.Bones` 查找 `Hand_WristRoot`。这是机械臂运动控制的基准点（TCP）。

### **B. 视觉影子：`HandUIProxy.cs`**

**功能：** 在 UI 面板中生成位置固定但姿态同步的“投影手”。

* **关键算法：影子骨骼重映射 (Proxy Bone Mapping)**
  * **实现：** 该脚本直接引用源骨骼的 Transform 数组 (`_proxyMeshRenderer.bones = _sourceSMR.bones`)。
  * **优势：** 这种做法绕过了手动克隆骨骼导致的权重丢失问题，确保手部 Mesh 在 UI 中能完美变形且无延迟。
  * **隔离逻辑：** 通过 `proxyLayer` 将模型隔离在 Layer 6/7，配合专用相机实现“画中画”效果。

### **C. 交互界面：`IronManHUD.cs`**

**功能：** 沉浸式战术 HUD，管理 RenderTexture 渲染和相机堆栈。

* **关键函数：**
  * `ExcludeProxyLayersFromMainCamera()`: 使用 **位运算算法** (`mainCam.cullingMask &= ~mask`) 动态修改渲染遮罩，彻底消除主视角的粉色重影。
  * `UpdateProxyCameras()`:  **动态对焦算法** 。相机使用 `LookAt(wrist.position)` 实时锁定手腕，确保手部始终位于 UI 框正中心。
  * `SetupPerEyeHandMaterials()`: 针对 VR 双目特性，为左右眼分别分配 `Custom/PerEyeHand` 材质。

### **D. 网络通信：`ROSBridgeConnection.cs`**

**功能：** 基于异步任务的高可靠 ROS 消息传输。

* **核心算法：指数退避重连 (Exponential Backoff)**
  * **实现：** 重连间隔随失败次数按 `Mathf.Pow(1.5f, attempts)` 指数增长，防止网络波动时请求过载。
  * **心跳算法：** `heartbeatFailThreshold` 逻辑。连续多次检测到 WebSocket 状态异常才会判定断连，提高了在移动 Wi-Fi 环境下的稳定性。
  * **JSON 优化：** 使用 `StringBuilder` 预分配 1024 字节内存，避免高频发送 Pose 数据（60Hz）造成的频繁 GC（内存垃圾回收）。
  * 
  * 
  * 1. 通信协议：**基础协议** ：系统使用 **WebSocket** 协议（通过 C# 的 `ClientWebSocket` 实现）连接到 `rosbridge_server`。**握手机制** ：在发送数据前，Unity 端会通过发送 `{"op":"advertise", ...}` 指令在 ROS 中注册话题。
    2. 数据结构与消息类型：数据通过 JSON 字符串封装，主要包含两类 ROS 标准消息：
  * **位姿数据 (`geometry_msgs/PoseStamped`)** ：
  * **结构** ：包含 `header`（含 `stamp` 时间戳和 `frame_id`）和 `pose`。
  * **具体字段** ：

    * `position`: 包含 `x`, `y`, `z` 坐标（保留 6 位小数）。
    * `orientation`: 包含四元数 `x`, `y`, `z`, `w`（保留 6 位小数）。
  * **话题** ：默认为 `/quest3/right_hand_pose` 和 `/quest3/left_hand_pose`。
  * **夹爪数据 (`std_msgs/Float32`)** ：
  * **结构** ：包含一个 `data` 字段。
  * **数值** ：0.0 到 1.0 之间的浮点数（保留 4 位小数），代表捏合强度。
  * **话题** ：默认为 `/quest3/right_gripper` 和 `/quest3/left_gripper`。
  * 3. 发送与处理频率：
  * **Unity 发送频率** ：通过 `TeleopManager.cs` 中的 `sendRate` 字段控制，默认设置为  **60Hz** 。
  * **网络循环** ：`ROSBridgeConnection.cs` 中的异步发送循环 (`SendLoopAsync`) 每隔 **10ms** 检查一次发送队列。
  * **ROS 处理频率** ：`teleop_node.py` 内部也设定了默认 **60.0Hz** 的发布频率，并设置了 1.0 秒的数据超时检测。
  * 4. 坐标系转换算法：虽然数据由 Unity 发出，但 Python 端的 `teleop_node.py` 负责了关键的算法转换：
  * **位置转换** ：将 Unity 的左手系（Y轴向上）转换为 ROS 的右手系（Z轴向上）：`ros_x = unity_z`, `ros_y = -unity_x`, `ros_z = unity_y`。
  * **四元数转换** ：同步进行了对应的旋转映射以保证坐标系对齐。


{
  "op": "publish",
  "topic": "/quest3/right_hand_pose",
  "msg": {
    "header": {
      "stamp": {
        "sec": 1739959860,
        "nanosec": 123000000
      },
      "frame_id": "quest3_right_hand"
    },
    "pose": {
      "position": {
        "x": 0.123456,
        "y": 0.456789,
        "z": -0.789012
      },
      "orientation": {
        "x": 0.0,
        "y": 0.0,
        "z": 0.0,
        "w": 1.0
      }
    }
  }
}

{
  "op": "publish",
  "topic": "/quest3/right_gripper",
  "msg": {
    "data": 0.8500
  }
}

### **E. 逻辑调度：`TeleopManager.cs`**

**功能：** 控制循环与安全策略。

* **核心算法 1：重锚点校准 (Re-Anchoring Logic)**
  * **实现：** 当 `ClutchEngaged` 切换为 `true` 时，调用 `ReAnchorBothHands()` 记录当前手部位姿为 `CalibrationOffset`。
  * **目的：** 消除初始连接时的位置突跳，实现“相对增量控制”。
* **核心算法 2：速度限制器 (Velocity Limiting)**
  * **函数：** `ApplyVelocityLimit()`。计算 `delta.magnitude / dt`，若超过 `maxVelocity` 则强制截断位移，防止机械臂发生危险的瞬时冲撞。
* **核心算法 3：空间约束 (Workspace Clamping)**
  * **函数：** `ClampToWorkspace()`。使用 `Mathf.Clamp` 将输出坐标限制在 3D 立方体边界内，保护机械臂不超出物理限位。

---

## **3. 综合评估**

1. **UI/渲染解耦：** 通过代理相机和 Layer 隔离，实现了极客感十足的战术 UI，且不干扰主视角的 Passthrough 透视。
2. **安全控制闭环：** 结合了离合器（Clutch）、速度限制、空间约束和重锚点校准四重保障，非常适合 **Doctor Octopus** 这种复杂的双臂机器人遥操作场景。
3. **高性能网络：** 异步 WebSocket 配合高效的字符串构建，能够支撑 120Hz 的高频指令发送。
