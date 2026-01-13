# -*- coding: utf-8 -*-
"""
iPhone LiDAR + MediaPipe Hand Tracking → Unity
Sends 3D hand joints only
"""

import numpy as np
import cv2
import csv
import socket
import struct
import time
from threading import Event
from record3d import Record3DStream
import mediapipe as mp

# MediaPipe Tasks API (NEW)
from mediapipe.tasks import python
from mediapipe.tasks.python import vision

# ----------------- MediaPipe Setup -----------------
MODEL_PATH = "hand_landmarker.task"  # MUST exist

BaseOptions = python.BaseOptions
HandLandmarker = vision.HandLandmarker
HandLandmarkerOptions = vision.HandLandmarkerOptions
VisionRunningMode = vision.RunningMode

options = HandLandmarkerOptions(
    base_options=BaseOptions(model_asset_path=MODEL_PATH),
    running_mode=VisionRunningMode.IMAGE,
    num_hands=2
)

hand_detector = HandLandmarker.create_from_options(options)

# ----------------- Main Class -----------------
class LiDARStreamer:
    def __init__(self, host="127.0.0.1", port=5500):
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.sock.connect((host, port))
        print("Connected to Unity")

        self.session = None
        self.event = Event()

    def connect_to_device(self):
        devices = Record3DStream.get_connected_devices()
        if not devices:
            raise RuntimeError("No Record3D devices found")

        self.session = Record3DStream()
        self.session.on_new_frame = self.on_new_frame
        self.session.connect(devices[0])

        started = False
        if hasattr(self.session, "start"):
            self.session.start()
            started = True
            print("Started Record3D stream via session.start()")
        elif hasattr(self.session, "start_streaming"):
            self.session.start_streaming()
            started = True
            print("Started Record3D stream via session.start_streaming()")
        else:
            print("No start/start_streaming on Record3DStream; assuming it auto-starts after connect()")

        print(f"Connected to iPhone LiDAR (streaming_started={started})")


    def on_new_frame(self):
        self.event.set()

    def run(self):
        print("Starting stream loop...")

        frame_id = 0
        sent_id = 0
        last_frame_time = time.perf_counter()

        # CSV logging
        log_file = open("lidar_mediapipe_log.csv", "w", newline="")
        csv_writer = csv.writer(log_file)
        csv_writer.writerow([
            "frame_id",
            "timestamp",
            "fps",
            "num_hands",
            "num_joints"
        ])

        try:
            while True:
                if hasattr(self.session, "wait_for_new_frame"):
                    self.session.wait_for_new_frame()
                else:
                    self.event.wait()
                    self.event.clear()
                rgb = self.session.get_rgb_frame()
                depth = self.session.get_depth_frame()
                
                if rgb is None or depth is None:
                    continue

                # ---------- Timing ----------
                now = time.perf_counter()
                fps = 1.0 / (now - last_frame_time)
                last_frame_time = now
                timestamp = now
                frame_id += 1

                # ---------- Depth cleanup ----------
                depth = np.nan_to_num(depth, nan=0.0)
                depth = np.clip(depth, 0.0, 5.0)

                # ---------- Rotate ----------
                rgb = cv2.rotate(rgb, cv2.ROTATE_90_COUNTERCLOCKWISE)
                depth = np.ascontiguousarray(
                    cv2.rotate(depth, cv2.ROTATE_90_COUNTERCLOCKWISE)
                )
                # ---------- MediaPipe ----------
                rgb_uint8 = rgb.astype(np.uint8)
                mp_image = mp.Image(
                    image_format=mp.ImageFormat.SRGB,
                    data=rgb_uint8
                )

                result = hand_detector.detect(mp_image)

                num_hands = 0
                num_joints = 0
                hands_payload = []

                if result.hand_landmarks:
                    num_hands = len(result.hand_landmarks)

                    h, w = depth.shape
                    for hand_landmarks in result.hand_landmarks:
                        joints_3d = []
                        for lm in hand_landmarks:
                            x_px = int(lm.x * w)
                            y_px = int(lm.y * h)
                            z = float(depth[y_px, x_px]) if 0 <= x_px < w and 0 <= y_px < h else 0.0
                            joints_3d.append([lm.x, lm.y, z])

                        num_joints += len(joints_3d)
                        hands_payload.append(str(joints_3d))

                # ---------- Send to Unity ----------
                payload_str = "||".join(hands_payload)
                payload = payload_str.encode("utf-8")

                packet = struct.pack("<Id", len(payload), timestamp) + payload
                self.sock.sendall(packet)

                sent_id += 1

                # ---------- CSV Log ----------
                csv_writer.writerow([
                    frame_id,
                    timestamp,
                    fps,
                    num_hands,
                    num_joints
                ])

                if sent_id % 30 == 0:
                    print(
                        f"Frames sent: {sent_id} | FPS: {fps:.1f} | Hands: {num_hands}"
                    )

        finally:
            log_file.close()

    def close(self):
        self.sock.close()
        print("Closed cleanly")


# ----------------- Entry -----------------
if __name__ == "__main__":
    streamer = LiDARStreamer()
    streamer.connect_to_device()

    try:
        streamer.run()
    except KeyboardInterrupt:
        streamer.close()
