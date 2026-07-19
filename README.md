# Vehicle Noise Synthesizer

https://github.com/user-attachments/assets/0140613e-f241-4938-af58-fb6ada2ee422

## ATTENTION

1. For the latest version, simply clone the repo or download the Unitypackage.
2. If you do not own an NWH Physics asset for your project, delete the two related scripts in the Input folder.

VNS is an open-source, free, multi-threaded async audio add-on primarily designed for Unity to simulate vehicle sounds based on engine RPM, similar to a granulator, and it has no dependencies such as FMOD. Originally designed for [my hobby project](https://atg-simulator.com/). It is best for transmission, exhaust, engine, and intake sounds and is intended for each of those audio sources separately.

![{ADC15B13-CDF1-412F-A49A-F42651C76447}](https://github.com/user-attachments/assets/f12cac05-6ac5-4c41-8234-d67f64bf8363)

```
Vehicle Sound Synthesizer • Enhanced Loop-based Vehicle Noise Simulator • v1.9f
```

### Installation:

1.   **Requirements:** Unity 2021.3 or newer. The `Unity.Mathematics` and `Unity.Burst` packages are required for the non-WebGL path.
2.   **Download:** Clone this repository directly into your Unity project's `Assets` folder. Do not use the old `.unitypackage` releases as they are outdated.
3.   **Setup:** Add the `VehicleNoiseSynthesizer` component to your vehicle's audio root GameObject.
4.   **Custom input scripts:** If you use a custom integration (not `AudioGranulatorNWHVehiclePhysics2`), you must call `OnThrottleTipIn`, `OnThrottleTipOut`, and `OnGearShift` at the appropriate moments, or the throttle-body and DCT shift burble effects will never play. Refer to the [Developer Reference](https://docs.atg-simulator.com/vehiclenoisesimulator) for the full API.

---

## Documentation

📚 **Full Developer Reference:** [docs.atg-simulator.com/vehiclenoisesimulator](https://docs.atg-simulator.com/vehiclenoisesimulator)

The developer reference includes:

- Complete parameter reference with defaults and ranges
- 3-tier pitch tuning guide
- Live inspector simulator (interactive)
- Public Event API documentation
- NWH Vehicle Physics 2 integration guide
- Setup instructions and common pitfalls

---

**_Can be used for:_**

☑️ Engine

☑️ Intake

☑️ Exhausts

☑️ Transmission⁰

☑️ Differential⁰ \*(and alike)\_

ℹ️ Uses real audio clips per Engine RPM and Engine Load to create realistic sound/noise. Two-neighbour constant-power crossfade with cylinder-aware pair hold timing.

---

**_Pros:_** _Some of the main reasons for devising this asset:_

✅ Lightweight - async Burst-compiled calculations per fixed delta time (non-WebGL), synchronous fallback for WebGL.

✅ Uses only Unity, optionally supports Unity Audio Mixer. Full WebGL compatibility.

✅ Advanced parameters to fine-tune the audio effect - per-clip min/max pitch, volume offset, pitch offset.

✅ Accurate constant-power crossfade blending between neighbouring RPM clips (cos² + sin² = 1).

✅ Refreshed custom inspector with active-pair volume envelope visualization and per-clip pitch range readouts.

✅ Cylinder-aware pair hold timing for realistic combustion-engine character.

---

**_Cons:_**

❌ Needs separate audio files per RPM¹

❌ Needs careful tweaking and spending time adjusting parameters to properly simulate a realistic vehicle sound with it

---

🔁 **_How to loop?_**

These are useful to compose loop audio for your vehicle sound simulation.

- There is a description of how to seamlessly loop audio using a free audio tool manually:
    - Intermediate Approach: https://gamedevbeginner.com/create-looping-sound-effects-for-games-for-free-with-audacity/
    - Advanced Approach: https://youtu.be/lMZXjCeAUPM
- This one is even easier; you don't need to download a tool, and it is online, but a bit more limited: https://www.drumbot.com/projects/looper/
- This uses paid software to correct the pitch and make a cleaner, seamless audio clip: https://youtu.be/1bnasSQbBqk

---

⚠️ **_Alternatives?_**

Although this asset was done with the idea of using it in [my passion project ATG Simulator](https://ATG-Simulator.com), the paid ones might be easier to work with. Below is a list of assets and plugins you can use instead - prices valid at the time of writing:

- [FMOD Indie _(Free under some limitations, needs your audio clips)_](https://www.fmod.com/download)
- Unity Asset: [**Realistic Engine Sounds** (from 23 US$ to +260 USD with most needed audio clips)](https://assetstore.unity.com/packages/tools/audio/realistic-engine-sounds-2-pro-edition-224783)
- Unity/Unreal/... Plugin: [**Crankcase Rev** (from 1,500 USD)](https://www.audiokinetic.com/en/products/plugins/crankcase-rev/)
- DAW Plugin _(Can mix both engine and exhaust sounds of different vehicles)_: [**Igniter** (430 USD)](https://www.krotosaudio.com/igniter/)
- DAW Plugin _(Recommended for engine sounds only)_: [**Audio Motors**](https://lesound.io/product/audiomotors-pro/)
- Unity Asset: [**Granular Synthesis for Engine Audio** (Free)](https://github.com/CombatWombatZockchster/Granular-Synthesis-for-Engine-Audio)
- Unity/Unreal _(Procedural Engine Sounds)_: [**Nemisindo Engine** (44 USD)](https://assetstore.unity.com/packages/tools/audio/nemisindo-engine-procedural-sound-effects-222246)
- Standalone _(Procedural Realistic Car Sounds, very CPU intensive)_: [**Engine Simulator** (Free)](https://www.engine-sim.parts/)
- Android App _(Limited predefined audio files, basic non-seamless loop)_: [**RevHeadz** (free but w/ in-app purchases)](https://rev-headz.com/)

Note that with DAW or standalone apps and plugins, you may record looped audio clips for this (or other assets) for simulating your audio in real-time.

---

ℹ️ **_How to use it?_**

Either download the UnityPackage or a zipped archive of this repo and import it into a new Unity project. This asset has demo scenes that need NWH Vehicle Physics and NWH Dynamic Water Physics. To use any demo scene, you will need those assets imported first. You may have none, one, or all of the NWH assets - just delete the scripts that are not relevant to your project (e.g., NWH Input demo samples if no NWH Asset is used).

1. Add the `VehicleNoiseSynthesizer` component to a GameObject
2. Assign an AudioSource Template and populate the Acceleration (and optionally Deceleration) clip banks
3. Set per-clip RPM values and min/max pitch ranges
4. Use the inspector blend visualization to verify your curves - the graph highlights the two active clips and dims inactive ones
5. Feed RPM and Load values via script (see `AudioGranulatorSimpleUI` for a test harness with dual Input System support, or the NWH adapters for real vehicle integration)
6. **Important:** If using a custom input script, call `OnThrottleTipIn`, `OnThrottleTipOut`, and `OnGearShift` to enable throttle-body and DCT effects. See the [Developer Reference](https://docs.atg-simulator.com/vehiclenoisesimulator) for the full API.

---

ℹ️ **_How does it work?_**

<img src="https://raw.githubusercontent.com/ATG-Simulator/VehicleNoiseSynthesizer/main/Depiction.jpg" alt="How does this asset work, Simplified in an image." width="65%">

Two neighbouring audio clips (lo / hi) are crossfaded at any given RPM using a constant-power cosine/sine pan law (`cos² + sin² = 1`). The active pair is selected by a hysteresis-buffered nearest-neighbour search with cylinder-aware hold timing. Per-clip pitch follows a continuous RPM-progress mapping. A Burst-compiled job system (non-WebGL) batches all synthesizer instances for maximum performance.

---

ℹ️ **_Why the heck am I sharing this freely?_**

I expect people to use this happily, but also to improve it and share an enhanced version with others. _Happy coding! :)_

---

©️ Dan. Several internet sources and GitHub Repositories like Keijiro Takahashi, CombatWombatZockchster, manueleisl, and others inspired the original script and its idea. The original idea was published as a GitHub repo to recreate a ForzaMotorsport4-type audio simulation for cars, which unfortunately I lost the URL².

Thanks for your attention.

_Check the social media of my passion project called ATG Simulator, please:_

- <https://twitter.com/atg_simulator>
- <https://www.instagram.com/atg_simulator/>
- <https://atg-simulator.com/>
- <https://youtube.com/@atg-simulator>
- <https://youtube.com/c/imdanoush>

---

**Footnotes:**

⁰ Transmission and differential sounds from the video are not included in this script. Also, blow-off and other sounds are not part of this asset, as they are not intended to be.

¹ This script is also inspired by this video of the Turn10 Audio Engineer: <https://youtu.be/UNvka9GL-9k>. Same as Forza Horizon 3 or Forza Motorsport 7, it needs audio clips based on different RPM speeds. _E.g. `Ferrari458Engine_Accelerating_at_the_rpm_speed_of_5000.wav`._ It needs at least one accelerating audio clip; very few or too many clips may result in subpar quality.

² In case you know the repository, feel free to let everyone know by adding a comment in the main GitHub repository.
