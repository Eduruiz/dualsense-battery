# DualSense Battery

A Windows system tray app that monitors your DualSense controller battery level and notifies you when it's running low.

> **Disclaimer:** This project was entirely vibe-coded with the help of AI. Use at your own risk.

## Features

- Battery level shown as a colored icon in the system tray (green / yellow / red)
- Hover the tray icon to see the current percentage
- Notification when the controller connects or disconnects
- Warning notification at 20% battery
- Critical notification at 5% battery (breaks through Do Not Disturb)
- Works over both USB and Bluetooth

## How to use

1. Download the latest `DualSenseBattery.App.exe` from the [Releases](https://github.com/Eduruiz/dualsense-battery/releases/latest) page
2. Run the `.exe` — no installation needed
3. The app will appear in the system tray and start monitoring automatically
4. To launch on startup, place a shortcut to the `.exe` in your Windows Startup folder (`Win + R` → `shell:startup`)

## Requirements

- Windows 10 / 11
- DualSense controller (USB or Bluetooth)
