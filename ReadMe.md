# AssistWakeWordSniffer 🎤

**AssistWakeWordSniffer** is a lightweight utility designed to monitor your Home Assistant **Assist** voice triggers. It maintains a constant 12-second "rolling buffer" of audio. When a trigger is detected, it creates and saves a .wav file that consists of ~5 seconds of audio that occurred before the trigger, ~2 of audio for the trigger itself, and ~5 seconds of audio that occurred after the trigger.

These audio files can then be used for micro wake word training with [microWakeWord-Trainer-Enhanced-Negatives](https://github.com/stevevib/microWakeWord-Trainer-Enhanced-Negatives) or simply to understand what your Assist device **heard** prior to activating.

<br>

## 🛠️ 1. Prerequisites

- **Docker Desktop** (Windows/Mac) or **Docker Engine** (Linux).

- **Home Assistant** instance on your local network.

- **FFmpeg (Windows / macOS / WSL2 Only):** Required on your host machine if you are using [Audio Setup Method B](#method-b-the-ffmpeg-bridge-windows--macos--wsl2).

<br>

## 🔑 2. Setup Home Assistant Authentication

The sniffer needs a **Long-Lived Access Token** to talk to your Home Assistant WebSocket.

1.  Log into **Home Assistant**.
2.  Click on your **Profile Name** (bottom-left sidebar).
3.  Scroll to the bottom to the **Long-Lived Access Tokens** section.
4.  Click **Create Token**, name it `AssistSniffer`, and **copy the token**.
    > ⚠️ **Important:** Copy the token immediately! You will not be able to see it again once you close the window.

<br>

## ⚙️ 3. Configuration

Create a folder on your computer to serve as your **Application Folder** (e.g., `C:\assist-sniffer` or `~/assist-sniffer`). 

Inside that folder, create a file named `.env`. This file stores your settings so they can be read by Docker.

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

# Appearance
# Set to 'true' if you see weird blocks instead of emojis in your logs
USE_ASCII_LOGS=false
```

<br>

## 🐋 4. Docker Compose Setup

> ℹ️ If you prefer to use the docker run command you can skip this section.  Using the docker run option is covered in the [Installation & Running](#option-b-using-docker-run) section.

Create a file named `docker-compose.yml` in the same directory. This defines how the container runs and links it to your `.env` file.

```yaml
services:
  assist-sniffer:
    container_name: assist-sniffer
    image: ghcr.io/stevevib/assistwakewordsniffer:latest
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

<br>

## 📍 5. Microphone Placement & Hardware

To capture accurate data for training, the sniffer's microphone should "shadow" your Voice Assistant satellite as closely as possible.

- **Proximity:** Place the sniffer microphone in close physical proximity to the voice satellite device. This ensures it hears the same environmental triggers (coughs, TV noise, etc.) at the same volume levels.

- **Hardware Choice:** Use an **omnidirectional** microphone. 

- **Processing:** Ideally, the microphone should have **minimal to no onboard noise cancellation or AGC (Auto Gain Control)**. You want the "raw" audio to closely approximate the raw audio being processed by the satellite's wake word engine.

### **Target Audio Levels**
For high-quality training samples, aim for the following ranges in your logs:
- **Peak:** Between **40% and 80%**. If you consistently hit 100%, your audio is "clipping" and losing detail.

- **RMS:** Between **10% and 30%**. This represents the average level of the audio signal.

- **Noise Floor (Min):** Ideally **below 1%**. High values here indicate a noisy room or electrical interference.

<br>

## 🎤 6. Audio Setup

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
> 💡 **Note:** The `-re` and `-thread_queue_size` flags ensure the stream stays in real-time (Speed=1.0x).
   - **Windows (PowerShell/CMD):**
     ```powershell
     ffmpeg -re -thread_queue_size 1024 -f dshow -i audio="Device Name" -f s16le -ac 1 -ar 16000 udp://127.0.0.1:1234
     ```
   - **macOS:**
     ```bash
     ffmpeg -re -f avfoundation -i ":0" -f s16le -ac 1 -ar 16000 udp://127.0.0.1:1234
     ```
   - **Linux/WSL2:**
     ```bash
     ffmpeg -re -f alsa -i default -f s16le -ac 1 -ar 16000 udp://127.0.0.1:1234
     ```
>⚠️ **Note:** This terminal window **must remain open** while the sniffer is running. Closing it stops the audio feed.

<br>

## 📡 7. Home Assistant Voice Assist Satellite Settings

To get the cleanest data possible, you'll need to temporarily adjust your satellite settings in Home Assistant:

- **Volume Muting:** Temporarily set the satellite's media player volume to **0**. Since the sniffer records 5 seconds *after* the trigger, muting the volume prevents the satellite's "ding" chime or TTS response from bleeding into the recording.

- **Negative Sample Capture:** If you are using the sniffer to collect "False Positive" data for training, **do not speak the actual wake word**. The goal is to capture the background noise, TV sounds, or similar-sounding words that accidentally triggered the device. This provides high-quality "negative" samples that help the model learn what *not* to trigger on.
 

>ℹ️ Remember to restore your satellite's volume after your capture session!

<br>

## 🚀 8. Installation & Running

Open a terminal in your **Application Folder** (where your `.env` and `docker-compose.yml` are located.

**Pull the latest image:**
   ```bash
   docker pull ghcr.io/stevevib/assistwakewordsniffer:latest
   ```

Run the container using one of the following methods:

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

<br>

## 🔍 9. Monitoring & Logs

To verify your connection or see the live volume meter:
```bash
docker logs -f assist-sniffer
```

**Success looks like:**
- `✅ Connected and Authenticated to HA.`
- `⚡ Wake Word Detected!`
- `💾 SAVE COMPLETE: centered_20240101_120000.wav`
- `📊 Levels -> Peak: 45% | RMS: 12% | Min: 0.5% (Full 12s Clip)`

### **Understanding the Stats**
- **Peak:** The loudest moment in the clip. If this is **100%**, your gain is too high and the audio is distorted.
- **RMS:** The average perceived loudness. Good for ensuring your voice isn't too quiet.
- **Min:** The quietest moment (noise floor). Useful for spotting background hum or hiss.
<br>

## 📂 10. Accessing Captures

Each time your Home Assistant Satellite triggers, a file is generated.

- **Location:** Look in the `captures/` folder that was automatically created inside your **Application Folder**.

- **Filename:** `centered_[timestamp].wav`

- **Logic:** Each clip is 10 seconds (5s pre-roll, 5s post-roll).

- **Cleanup:** Files older than **72 hours** are automatically deleted to save space.

<br>

## 🛠️ 11. Troubleshooting

### **Emoji & Encoding Issues**
- **Weird characters in logs:** Many Windows terminals and older Docker logs do not support Unicode emojis. 
  
  - - **Fix:** Set `USE_ASCII_LOGS=true` in your `.env` file. This will replace icons with text tags like `[OK]`, `[ERROR]`, and `[WAIT]`.

### **WebSocket Issues**
- **"Authentication Failed":** Ensure your `HA_TOKEN` has no extra spaces and is a valid "Long-Lived Access Token."

### **Audio Issues**
- **No Volume Meter activity:**
  
  - In Bridge Mode (Method B), ensure FFmpeg is running on your host. 
  
  - In Hardware Mode (Method A), ensure the `AUDIO_DEVICE_NAME` matches exactly what `arecord -L` shows inside the container.

- **"Device in use":** Only one application can control a sound card at a time. Ensure no other applications are using the microphone on the host.

### **Docker Issues**
- **Captures folder is empty:** Check permissions. Docker needs write access to the folder where `docker-compose.yml` resides.
- **Permission Denied (/dev/snd):** On Linux, your user may need to be in the `audio` group (`sudo usermod -aG audio $USER`).

<br>

## 🛑 12. Stopping the Sniffer

To stop and remove the container:\
```docker compose down```
#### OR
```docker stop assist-sniffer && docker rm assist-sniffer```