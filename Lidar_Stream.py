# -*- coding: utf-8 -*-
"""
iPhone LiDAR streaming to Unity
Sends raw RGB and depth bytes over TCP
"""

import numpy as np
import cv2
from record3d import Record3DStream
from threading import Event
import socket
import struct
import time

class LiDARStreamer:
    def __init__(self, host, port, device_index=0, rotation_angle=0):
        # ----------------- TCP -----------------
        self.host = host
        self.port = port
        self.sock = None
        self.connected = False

        # ----------------- Image Processing -----------------
        self.rotation_angle = rotation_angle  # 0, 90, 180, 270
        
        # ----------------- LiDAR -----------------
        self.device_index = device_index
        self.session = None
        self.event = Event()

    def rotate_image(self, image, angle):
        """Rotate image by specified angle (0, 90, 180, 270 degrees)"""
        if angle == 0:
            return image
        elif angle == 90:
            return cv2.rotate(image, cv2.ROTATE_90_CLOCKWISE)
        elif angle == 180:
            return cv2.rotate(image, cv2.ROTATE_180)
        elif angle == 270:
            return cv2.rotate(image, cv2.ROTATE_90_COUNTERCLOCKWISE)
        else:
            print(f"Warning: Unsupported rotation angle {angle}. Using 0 degrees.")
            return image

    def connect_to_unity(self):
        """Connect to Unity server with retry logic"""
        max_retries = 10
        retry_delay = 2
        
        for attempt in range(max_retries):
            try:
                print(f"Attempting to connect to Unity at {self.host}:{self.port} (attempt {attempt + 1}/{max_retries})...")
                self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
                self.sock.connect((self.host, self.port))
                self.connected = True
                print(f"Successfully connected to Unity!")
                return True
            except ConnectionRefusedError:
                print(f"Unity server not ready, retrying in {retry_delay} seconds...")
                if self.sock:
                    self.sock.close()
                time.sleep(retry_delay)
            except Exception as e:
                print(f"Connection error: {e}")
                if self.sock:
                    self.sock.close()
                time.sleep(retry_delay)
        
        print("Failed to connect to Unity after all retries")
        return False

    def connect_to_device(self):
        """Connect to Record3D device"""
        devices = Record3DStream.get_connected_devices()
        if not devices:
            raise RuntimeError("No Record3D devices found")
        if len(devices) <= self.device_index:
            raise RuntimeError(f"Cannot connect to device #{self.device_index}")

        dev = devices[self.device_index]
        print(f"Connecting to device {dev.product_id} ({dev.udid})...")
        self.session = Record3DStream()
        self.session.on_new_frame = self.on_new_frame
        self.session.on_stream_stopped = self.on_stream_stopped
        self.session.connect(dev)
        print(f"Connected to LiDAR device. Rotation set to {self.rotation_angle} degrees.")

    def on_new_frame(self):
        self.event.set()

    def on_stream_stopped(self):
        print("LiDAR stream stopped")

    def send_frame(self, depth, rgb):
        """Send frame data to Unity"""
        if not self.connected:
            return False
            
        try:
            # Apply rotation to both images
            if self.rotation_angle != 0:
                depth = self.rotate_image(depth, self.rotation_angle)
                rgb = self.rotate_image(rgb, self.rotation_angle)
            
            # Convert depth to millimeters and ensure uint16
            depth_mm = (depth * 1000).astype(np.uint16)
            
            # Ensure RGB is uint8
            rgb_uint8 = rgb.astype(np.uint8)
            
            # Flatten arrays and convert to bytes
            rgb_bytes = rgb_uint8.flatten().tobytes()
            depth_bytes = depth_mm.flatten().tobytes()

            # Packet: [width:int][height:int][depth bytes][rgb bytes]
            height, width = depth.shape
            packet = struct.pack("<II", width, height) + depth_bytes + rgb_bytes
            
            # Send packet length first, then the packet
            packet_length = len(packet)
            length_bytes = struct.pack("<I", packet_length)
            
            self.sock.sendall(length_bytes + packet)
            return True
            
        except Exception as e:
            print(f"Error sending frame: {e}")
            self.connected = False
            return False

    def run(self):
        """Main streaming loop"""
        print("Starting streaming loop...")
        frame_count = 0
        
        while True:
            try:
                if not self.event.wait(timeout=1.0):
                    continue
                    
                if not self.session:
                    continue
                    
                # Get frames from Record3D
                depth = self.session.get_depth_frame()
                rgb = self.session.get_rgb_frame()
                
                if depth is None or rgb is None:
                    continue
                
                # Resize RGB to match depth dimensions
                rgb_resized = cv2.resize(rgb, (depth.shape[1], depth.shape[0]))
                
                # Send to Unity
                if self.send_frame(depth, rgb_resized):
                    frame_count += 1
                    if frame_count % 30 == 0:  # Log every 30 frames
                        print(f"Sent {frame_count} frames to Unity (rotation: {self.rotation_angle}°)")
                else:
                    print("Failed to send frame, attempting reconnection...")
                    if self.connect_to_unity():
                        continue
                    else:
                        break
                        
                self.event.clear()
                
            except KeyboardInterrupt:
                print("Stopping streaming...")
                break
            except Exception as e:
                print(f"Error in streaming loop: {e}")
                break
    
    def close(self):
        """Clean up resources"""
        self.connected = False
        if self.sock:
            self.sock.close()
        if self.session:
            self.session.stop_stream()

if __name__ == "__main__":
    host = "127.0.0.1"
    port = 5500
    
    # Set rotation angle: 0, 90, 180, or 270 degrees
    rotation_angle = 90  # Change this to rotate your image
    
    print(f"Starting LiDAR streamer with {rotation_angle}° rotation")
    streamer = LiDARStreamer(host, port, rotation_angle=rotation_angle)
    
    try:
        # First start Unity and wait for it to be ready
        print("Make sure Unity is running and LidarReceiver is active!")
        input("Press Enter when Unity is ready...")
        
        # Connect to Unity first
        if not streamer.connect_to_unity():
            print("Failed to connect to Unity")
            exit(1)
        
        # Then connect to LiDAR device
        streamer.connect_to_device()
        
        # Start streaming
        streamer.run()
        
    except Exception as e:
        print(f"Error: {e}")
    finally:
        streamer.close()