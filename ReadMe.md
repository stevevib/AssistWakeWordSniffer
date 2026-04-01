# AssistWakeWordSniffer 🎤

**AssistWakeWordSniffer** is a lightweight utility designed to monitor your Home Assistant **Assist** voice triggers. It maintains a constant 10-second "rolling buffer" of audio. When a trigger is detected, it snaps that audio window (5 seconds before the trigger and 5 seconds after) and saves it as a `.wav` file for review.  

These audio files can then be used for micro wake word training with [microWakeWord-Trainer-Enhanced-Negatives](https://github.com/stevevib/microWakeWord-Trainer-Enhanced-Negatives) or simply to understand what your Assist device **heard** prior to activating.

---

## 🛠️ 1. Prerequisites

- **Docker Desktop** (Windows/Mac) or **Docker Engine** (Linux).
- **Home Assistant** instance on your local network.
- **FFmpeg (Windows / macOS / WSL2 Only):** Required on your host machine if you are using [Audio Setup Method B](#method-b-the-ffmpeg-bridge-windows--macos--wsl2).

---

## 🔑 2. Setup Home Assistant Authentication

The sniffer needs a **Long-Lived Access Token** to talk to your Home Assistant WebSocket.

1.  Log into **Home Assistant**.
2.  Click on your **Profile Name** (bottom-left sidebar).
3.  Scroll to the bottom to the **Long-Lived Access Tokens** section.
4.  Click **Create Token**, name it `AssistSniffer`, and **copy the token**.
    > ⚠️ **Important:** Copy the token immediately! You will not be able to see it again once you close the window.

---

## ⚙️ 3. Configuration

Create a file named `.env` in the root directory. This maps your hardware and Home Assistant settings into the container:

```bash
# Home Assistant Connection
HA_URL=ws://192.168.1.XXX:8123/api/websocket
HA_TOKEN=your_long_lived_access_token_here
HA_SATELLITE_ID=assist_satellite.your_device_id

# Audio Hardware Settings

# Name of the mic as seen by the OS (e.g. "Microphone (USB Audio Device)")
AUDIO_DEVICE_NAME="Microphone Name"

# Seconds of audio to keep in RAM (should be >10 for safe centering)
AUDIO_SECONDS_TO_BUFFER=20

# 'false' for direct hardware, 'true' if using the FFmpeg UDP Bridge
DEBUG_UDP_AUDIO=false
```

---

## 🐋 4. Docker Compose Setup

> ℹ️ If you prefer to use the docker run command you can skip this section.  Using the docker run option is covered in the [Installation & Running](#option-b-using-docker-run) section.

Create a file named `docker-compose.yml` in the same directory. This defines how the container runs and links it to your `.env` file.

```yaml
services:
  assist-sniffer:
    container_name: assist-sniffer
    image: stevevib/assist-sniffer:latest
    restart: unless-stopped
    env_file: .env
    ports:
      - "1234:1234/udp" # Required for UDP Bridge -- See "Method B" in the Audio Setup section
    volumes:
      - ./captures:/app/captures
    # Uncomment the following lines only if using Direct Hardware -- See "Method A" in the Audio Setup section
    # devices:
    #   - "/dev/snd:/dev/snd"
```

---
## 🎤 5. Audio Setup

Depending on your Operating System, choose **one** of the following paths to get audio into the sniffer.

### **Method A: Direct Hardware (Native Linux / Raspberry Pi)**
If running on native Linux, the container accesses your USB microphone directly.

1. In `.env`, set `DEBUG_UDP_AUDIO=false`.
2. In your `docker-compose.yml`, uncomment the `devices` section shown in step 4.
3. **Windows Users:** To use this method, you must install [usbipd-win](https://github.com/dorssel/usbipd-win) to "attach" your USB mic to the WSL2 kernel.

### **Method B: The FFmpeg Bridge (Windows / macOS / WSL2)**
Docker on Windows and Mac cannot see USB microphones directly. This method "streams" audio from your host machine into the container via UDP.

1. In `.env`, set `DEBUG_UDP_AUDIO=true`.
2. **Find your microphone's system name** by running the command for your OS in a terminal:
   - **Windows:** `ffmpeg -list_devices true -f dshow -i dummy`
   - **macOS:** `ffmpeg -f avfoundation -list_devices true -i ""`
   - **Linux/WSL2:** `arecord -l` or `ffmpeg -f alsa -list_devices true -i dummy`

3. **Start the Bridge:** Run the command for your OS (replacing `"Device Name"` with the microphone's system name identified in the previous step):
   - **Windows (PowerShell/CMD):**
     ```powershell
     ffmpeg -f dshow -i audio="Device Name" -f s16le -ac 1 -ar 16000 udp://127.0.0.1:1234
     ```
   - **macOS:**
     ```bash
     ffmpeg -f avfoundation -i ":0" -f s16le -ac 1 -ar 16000 udp://127.0.0.1:1234
     ```
   - **Linux/WSL2:**
     ```bash
     ffmpeg -f alsa -i default -f s16le -ac 1 -ar 16000 udp://127.0.0.1:1234
     ```
   > ⚠️ **Note:** This terminal window **must remain open** while the sniffer is running. Closing it stops the audio feed.

---

## 📡 6. Configuring Your Assist Satellite
To get the cleanest data possible, you'll need to temporarily adjust your satellite settings in Home Assistant:

- **Volume Muting:** Temporarily set the satellite's media player volume to **0**. Since the sniffer records 5 seconds *after* the trigger, muting the volume prevents the satellite's "ding" chime or TTS response from bleeding into the recording.
- **Negative Sample Capture:** If you are using the sniffer to collect "False Positive" data for training, **do not speak the actual wake word**. The goal is to capture the background noise, TV sounds, or similar-sounding words that accidentally triggered the device. This provides high-quality "negative" samples that help the model learn what *not* to trigger on.

---

## 🚀 7. Installation & Running

Ensure your `.env` and `docker-compose.yml` (if using Option A) are ready.

### Option A: Using Docker Compose (Recommended)
Run the following command in your terminal:
```bash
docker compose up -d
```

### Option B: Using Docker Run
If you prefer a single command over a compose file, use the following. 

**Note:** If you are using **Audio Setup Method A** (Direct Hardware), you must add the `--device /dev/snd:/dev/snd` flag to the command below.

```bash
docker run -d \
  --name assist-sniffer \
  --restart unless-stopped \
  --env-file .env \
  -p 1234:1234/udp \
  -v ${PWD}/captures:/app/captures \
  stevevib/assist-sniffer:latest
```

---

## 🚀 8. Installation & Running

Ensure your `.env` and `docker-compose.yml` are ready, then run:
```bash
docker compose up -d
```

---

## 📂 9. Accessing Captures

Each time your Home Assistant Satellite triggers, a file is generated.

- **Location:** Check the `./captures/` folder on your host machine.
- **Filename:** `centered_[timestamp].wav`
- **Logic:** Each clip is 10 seconds (5s pre-roll, 5s post-roll).
- **Cleanup:** Files older than **72 hours** are automatically deleted to save space.

---

## 🔍 10. Monitoring & Logs

To verify your connection or see the live volume meter:
```bash
docker logs -f assist-sniffer
```

**Success looks like:**

Host is connected to Home Assistant:\
`✅ Connected and Authenticated to HA.`\

Home Assistant Satellite detected:\
`📡 Subscribed to Satellite: assist_satellite.your_device`

Microphone is streaming audio:\
`Volume: [██████░░░░░░░░] 06800`

---

## 🛑 8. Stopping the Sniffer

To stop and remove the container:\
```docker compose down```
#### OR
```docker stop assist-sniffer && docker rm assist-sniffer```