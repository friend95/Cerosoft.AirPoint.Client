# Cerosoft AirPoint Client (Android)

> **Remote input controller for Windows – built with .NET MAUI**  
> Your phone becomes a precision touchpad, gesture surface, and control hub.

---

## Overview
**Cerosoft AirPoint Client** is the Android-side application of the AirPoint ecosystem. It connects to the AirPoint Server running on Windows and sends high‑fidelity touch, gesture, and control input in real time.

This is not a gimmick remote app — it’s designed as a serious input surface with predictable behavior, low latency, and system-level intent.

---

## Ecosystem
AirPoint is a **client–server system**:

- **Server (Windows / WPF)** – Executes native mouse & system input  
  👉 https://github.com/friend95/Cerosoft.AirPoint

- **Client (Android / MAUI)** – Generates touch, gesture, and control signals  
  👉 https://github.com/friend95/Cerosoft.AirPoint.Client

Both repositories are required for the full experience.

---

## Key Features
- 📱 **Phone as a precision touchpad**
- 🖱️ **Remote mouse movement & clicks**
- 🤏 **Multi‑gesture surface (extensible)**
- ⚡ **Low‑latency communication**
- 🎛️ **In‑app settings & tuning**
- 🧠 **Clean page‑based architecture**

---

## Tech Stack
- **.NET MAUI**
- **C#**
- **XAML**
- **Android**

Built with cross‑platform discipline but optimized for Android behavior.

---

## Project Structure
```
Cerosoft.AirPoint.Client
│
├── MauiProgram.cs          # App bootstrap & DI
├── AirPointClient.cs       # Core client logic
├── MainPage.xaml           # Entry page
├── MainPage.xaml.cs
├── HomePage.xaml           # Main control UI
├── HomePage.xaml.cs
├── TouchpadView.cs         # Custom touchpad + gesture handling
├── SettingsPage.xaml       # Client configuration UI
├── SettingsPage.xaml.cs
└── Cerosoft.AirPoint.Client.csproj
```

---

## Getting Started

### Prerequisites
- Android device (Android 8.0+ recommended)
- Visual Studio 2022+ with **.NET MAUI** workload
- AirPoint Server running on the same network

### Build & Run
```bash
# Clone the repository
git clone https://github.com/friend95/Cerosoft.AirPoint.Client.git

# Open in Visual Studio
# Select Android target
# Build → Deploy
```

Ensure the **AirPoint Server** is running before attempting to connect.

---

## Touchpad & Gestures
The heart of the client lives in:
```
TouchpadView.cs
```

This layer:
- Translates raw touch input into intent
- Normalizes movement and gestures
- Sends clean, deterministic signals to the server

Designed to be extended without breaking existing behavior.

---

## Configuration
Client‑side preferences are handled via:
- `SettingsPage.xaml`
- `SettingsPage.xaml.cs`

All tuning stays explicit and debuggable — no hidden state.

---

## Design Principles
- **Input fidelity > visual noise**
- **Predictable behavior over flashy UI**
- **Latency awareness everywhere**
- **Server‑first architecture**

This app exists to *serve the system*, not distract from it.

---

## Security Notes
- Intended for trusted local networks
- No telemetry
- No background data harvesting

If you expose this beyond LAN, add authentication and encryption.

---

## Roadmap
- [ ] Advanced multi‑finger gestures
- [ ] Haptic feedback tuning
- [ ] Connection auto‑discovery
- [ ] Tablet‑optimized layout

---

## License
MIT License

---

## Author
**Cerosoft**  
Engineering‑first. System‑minded.

---

> A phone is just a sensor array — AirPoint turns it into a real input device.

