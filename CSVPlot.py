import pandas as pd
import matplotlib.pyplot as plt

# Load Unity log
df = pd.read_csv("apple-lidar-stream\\LidarStreaming\\Assets\\unity_hand_tracking_log.csv")

# Basic stats
print(df["latency_ms"].describe())
print("Mean FPS:", df["fps"].mean())

# ---- Plot 1: Latency over time ----
plt.figure()
plt.plot(df["latency_ms"])
plt.title("End-to-End Latency Over Time")
plt.xlabel("Frame")
plt.ylabel("Latency (ms)")
plt.grid()
plt.show()

# ---- Plot 2: FPS distribution ----
plt.figure()
plt.hist(df["fps"], bins=30)
plt.title("Tracking FPS Distribution")
plt.xlabel("FPS")
plt.ylabel("Count")
plt.grid()
plt.show()

# ---- Plot 3: Hand detection reliability ----
plt.figure()
plt.plot(df["num_hands"] > 0)
plt.title("Hand Detection Over Time")
plt.xlabel("Frame")
plt.ylabel("Detected (1=yes)")
plt.show()
