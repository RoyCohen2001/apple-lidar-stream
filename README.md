
# LiDAR Hand Tracking
Track your hand in real time using an iPhone’s LiDAR sensor with Record3D.

## Overview
* Record3D streams LiDAR data from your phone to your PC.
* A Python script processes the data.
* A Unity project runs a server that receives the stream and visualizes the hand.

## Usage
To start testing this you'll need to install the requirements from *requirements.txt*. You should also install Record3D on your iPhone pro model (only works with iPhone pro models and iPad models that have LiDAR). Record3D can be installed for free but to get the best performance you'll need the paid version. 

Connect the phone to the pc and start the unity project. Switch over to python and start *Lidar_Stream.py* project, everything should work now. Once done you can check the logs by starting *CSVPlot.py*.  
