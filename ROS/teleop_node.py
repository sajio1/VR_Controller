#!/usr/bin/env python3
"""
Quest 3 → OpenArm P1 遥操作 ROS 2 节点

功能：
  1. 订阅 Quest 3 通过 rosbridge 发来的双手位姿和夹爪数据
  2. 坐标系转换：Unity 左手系 Y-up → ROS 右手系 Z-up
  3. 安全检查：位置跳变检测、超时无数据自动停止
  4. 发布转换后的目标位姿到 OpenArm 控制话题

话题映射：
  右手：/quest3/right_hand_pose → /openarm/right_target_pose
  右手：/quest3/right_gripper   → /openarm/right_gripper_cmd
  左手：/quest3/left_hand_pose  → /openarm/left_target_pose
  左手：/quest3/left_gripper    → /openarm/left_gripper_cmd

坐标转换规则 (Unity → ROS)：
  位置：ros_x = unity_z, ros_y = -unity_x, ros_z = unity_y
  四元数：ros_qx = unity_qz, ros_qy = -unity_qx, ros_qz = unity_qy, ros_qw = -unity_qw

使用方法：
  ros2 run <your_package> teleop_node
  或直接：
  python3 teleop_node.py
"""

import math
import time

import rclpy
from rclpy.node import Node
from rclpy.qos import QoSProfile, ReliabilityPolicy, HistoryPolicy

from geometry_msgs.msg import PoseStamped
from std_msgs.msg import Float32


class TeleopNode(Node):
    """Quest 3 双手遥操作桥接节点"""

    def __init__(self):
        super().__init__('quest3_teleop_node')

        # ─────────── ROS 参数 ───────────
        # 右手话题
        self.declare_parameter('input_right_pose_topic', '/quest3/right_hand_pose')
        self.declare_parameter('input_right_gripper_topic', '/quest3/right_gripper')
        self.declare_parameter('output_right_pose_topic', '/openarm/right_target_pose')
        self.declare_parameter('output_right_gripper_topic', '/openarm/right_gripper_cmd')

        # 左手话题
        self.declare_parameter('input_left_pose_topic', '/quest3/left_hand_pose')
        self.declare_parameter('input_left_gripper_topic', '/quest3/left_gripper')
        self.declare_parameter('output_left_pose_topic', '/openarm/left_target_pose')
        self.declare_parameter('output_left_gripper_topic', '/openarm/left_gripper_cmd')

        # 安全参数
        self.declare_parameter('max_position_jump', 0.15)       # 最大允许单次跳变 (m)
        self.declare_parameter('data_timeout', 1.0)             # 无数据超时 (s)
        self.declare_parameter('publish_rate', 60.0)            # 发布频率 (Hz)

        # 读取参数
        in_r_pose = self.get_parameter('input_right_pose_topic').value
        in_r_grip = self.get_parameter('input_right_gripper_topic').value
        out_r_pose = self.get_parameter('output_right_pose_topic').value
        out_r_grip = self.get_parameter('output_right_gripper_topic').value

        in_l_pose = self.get_parameter('input_left_pose_topic').value
        in_l_grip = self.get_parameter('input_left_gripper_topic').value
        out_l_pose = self.get_parameter('output_left_pose_topic').value
        out_l_grip = self.get_parameter('output_left_gripper_topic').value

        self.max_position_jump = self.get_parameter('max_position_jump').value
        self.data_timeout = self.get_parameter('data_timeout').value
        publish_rate = self.get_parameter('publish_rate').value

        # ─────────── QoS ───────────
        qos = QoSProfile(
            reliability=ReliabilityPolicy.RELIABLE,
            history=HistoryPolicy.KEEP_LAST,
            depth=10
        )

        # ─────────── 右手订阅/发布 ───────────
        self.right_pose_sub = self.create_subscription(
            PoseStamped, in_r_pose, self.right_pose_callback, qos)
        self.right_gripper_sub = self.create_subscription(
            Float32, in_r_grip, self.right_gripper_callback, qos)

        self.right_pose_pub = self.create_publisher(PoseStamped, out_r_pose, qos)
        self.right_gripper_pub = self.create_publisher(Float32, out_r_grip, qos)

        # ─────────── 左手订阅/发布 ───────────
        self.left_pose_sub = self.create_subscription(
            PoseStamped, in_l_pose, self.left_pose_callback, qos)
        self.left_gripper_sub = self.create_subscription(
            Float32, in_l_grip, self.left_gripper_callback, qos)

        self.left_pose_pub = self.create_publisher(PoseStamped, out_l_pose, qos)
        self.left_gripper_pub = self.create_publisher(Float32, out_l_grip, qos)

        # ─────────── 内部状态 ───────────
        # 右手
        self.last_right_ros_pose = None
        self.last_right_gripper = 0.0
        self.last_right_pose_time = 0.0
        self.last_right_gripper_time = 0.0
        self.right_data_active = False

        # 左手
        self.last_left_ros_pose = None
        self.last_left_gripper = 0.0
        self.last_left_pose_time = 0.0
        self.last_left_gripper_time = 0.0
        self.left_data_active = False

        # ─────────── 超时检测定时器 ───────────
        timer_period = 1.0 / publish_rate
        self.timer = self.create_timer(timer_period, self.timer_callback)

        self.get_logger().info('=== Quest 3 双手遥操作节点已启动 ===')
        self.get_logger().info(f'  右手订阅位姿: {in_r_pose}')
        self.get_logger().info(f'  右手订阅夹爪: {in_r_grip}')
        self.get_logger().info(f'  右手发布位姿: {out_r_pose}')
        self.get_logger().info(f'  右手发布夹爪: {out_r_grip}')
        self.get_logger().info(f'  左手订阅位姿: {in_l_pose}')
        self.get_logger().info(f'  左手订阅夹爪: {in_l_grip}')
        self.get_logger().info(f'  左手发布位姿: {out_l_pose}')
        self.get_logger().info(f'  左手发布夹爪: {out_l_grip}')
        self.get_logger().info(f'  最大跳变: {self.max_position_jump} m')
        self.get_logger().info(f'  数据超时: {self.data_timeout} s')

    # ─────────── 坐标转换 ───────────

    @staticmethod
    def unity_to_ros_position(ux, uy, uz):
        """Unity 左手系 Y-up → ROS 右手系 Z-up 位置转换"""
        ros_x = uz
        ros_y = -ux
        ros_z = uy
        return ros_x, ros_y, ros_z

    @staticmethod
    def unity_to_ros_quaternion(uqx, uqy, uqz, uqw):
        """Unity 左手系 Y-up → ROS 右手系 Z-up 四元数转换"""
        ros_qx = uqz
        ros_qy = -uqx
        ros_qz = uqy
        ros_qw = -uqw
        return ros_qx, ros_qy, ros_qz, ros_qw

    # ─────────── 安全检查 ───────────

    def check_position_jump(self, new_pose: PoseStamped, last_pose) -> bool:
        """检测位置是否存在异常跳变，返回 True 表示安全"""
        if last_pose is None:
            return True

        dx = new_pose.pose.position.x - last_pose.pose.position.x
        dy = new_pose.pose.position.y - last_pose.pose.position.y
        dz = new_pose.pose.position.z - last_pose.pose.position.z
        distance = math.sqrt(dx * dx + dy * dy + dz * dz)

        if distance > self.max_position_jump:
            self.get_logger().warn(
                f'位置跳变检测: {distance:.4f} m > {self.max_position_jump} m，已丢弃')
            return False

        return True

    # ─────────── 通用位姿处理 ───────────

    def _process_pose(self, msg: PoseStamped, last_pose, hand_name: str):
        """处理位姿消息：坐标转换 + 安全检查，返回转换后的 ROS PoseStamped 或 None"""
        # Unity 坐标
        ux = msg.pose.position.x
        uy = msg.pose.position.y
        uz = msg.pose.position.z
        uqx = msg.pose.orientation.x
        uqy = msg.pose.orientation.y
        uqz = msg.pose.orientation.z
        uqw = msg.pose.orientation.w

        # 转换到 ROS 坐标系
        rx, ry, rz = self.unity_to_ros_position(ux, uy, uz)
        rqx, rqy, rqz, rqw = self.unity_to_ros_quaternion(uqx, uqy, uqz, uqw)

        # 构建 ROS PoseStamped
        ros_pose = PoseStamped()
        ros_pose.header.stamp = self.get_clock().now().to_msg()
        ros_pose.header.frame_id = 'base_link'
        ros_pose.pose.position.x = rx
        ros_pose.pose.position.y = ry
        ros_pose.pose.position.z = rz
        ros_pose.pose.orientation.x = rqx
        ros_pose.pose.orientation.y = rqy
        ros_pose.pose.orientation.z = rqz
        ros_pose.pose.orientation.w = rqw

        # 安全检查 — 跳变检测
        if not self.check_position_jump(ros_pose, last_pose):
            return None

        return ros_pose

    # ─────────── 右手回调 ───────────

    def right_pose_callback(self, msg: PoseStamped):
        """收到右手位姿数据的回调"""
        self.last_right_pose_time = time.time()

        ros_pose = self._process_pose(msg, self.last_right_ros_pose, '右手')
        if ros_pose is None:
            return

        self.last_right_ros_pose = ros_pose
        self.right_data_active = True
        self.right_pose_pub.publish(ros_pose)

    def right_gripper_callback(self, msg: Float32):
        """收到右手夹爪值的回调"""
        self.last_right_gripper_time = time.time()
        self.last_right_gripper = msg.data

        out_msg = Float32()
        out_msg.data = max(0.0, min(1.0, msg.data))
        self.right_gripper_pub.publish(out_msg)

    # ─────────── 左手回调 ───────────

    def left_pose_callback(self, msg: PoseStamped):
        """收到左手位姿数据的回调"""
        self.last_left_pose_time = time.time()

        ros_pose = self._process_pose(msg, self.last_left_ros_pose, '左手')
        if ros_pose is None:
            return

        self.last_left_ros_pose = ros_pose
        self.left_data_active = True
        self.left_pose_pub.publish(ros_pose)

    def left_gripper_callback(self, msg: Float32):
        """收到左手夹爪值的回调"""
        self.last_left_gripper_time = time.time()
        self.last_left_gripper = msg.data

        out_msg = Float32()
        out_msg.data = max(0.0, min(1.0, msg.data))
        self.left_gripper_pub.publish(out_msg)

    # ─────────── 超时检测 ───────────

    def timer_callback(self):
        """定时器回调 — 超时检测"""
        now = time.time()

        # 右手超时
        if self.right_data_active:
            if self.last_right_pose_time > 0 and (now - self.last_right_pose_time) > self.data_timeout:
                self.right_data_active = False
                self.get_logger().warn(
                    f'右手位姿数据超时 ({self.data_timeout}s 无数据)，已暂停')

        # 左手超时
        if self.left_data_active:
            if self.last_left_pose_time > 0 and (now - self.last_left_pose_time) > self.data_timeout:
                self.left_data_active = False
                self.get_logger().warn(
                    f'左手位姿数据超时 ({self.data_timeout}s 无数据)，已暂停')


def main(args=None):
    rclpy.init(args=args)
    node = TeleopNode()

    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        node.get_logger().info('节点被用户中断')
    finally:
        node.destroy_node()
        rclpy.shutdown()


if __name__ == '__main__':
    main()
