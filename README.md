# Vehicle Noise Synthesizer

## ATTENTION

1. For the latest version, simply clone the repo, or download the Unitypackage.
2. The Red Sports Car Demo for the NWH Vehicle Physics 2 scene is not properly set up and therefore outputs subpar audio.
3. If you do not own an NWH Physics asset for your project, delete the two related scripts in the Input folder.

VNS is an open-source free audio addon primarily designed for Unity to simulate vehicle sounds based on engine RPM, similar to a granulator, without dependencies such as FMOD.

![{ADC15B13-CDF1-412F-A49A-F42651C76447}](https://github.com/user-attachments/assets/f12cac05-6ac5-4c41-8234-d67f64bf8363)

```
An Enhanced FM4-Inspired Vehicle Sound Synthesizer  •  v1.8f2
```

---

### Latest: v1.8f2 (May 2026)

This release is a magor performance upgrade as it is now multi-threaded as well as it has deep-stability and accuracy update. The Non-WebGL (Burst) audio path now produces identical, correct results to the WebGL path. Every core subsystem - blend math, pitch mapping, crossfade panning, hysteresis, and editor tooling - has been audited and fixed against actual runtime behaviour.

**Installation:**

1.  **Requirements:** Unity 2021.3 or newer. The `Unity.Mathematics` and `Unity.Burst` packages are required for the non-WebGL path.
2.  **Download:** Clone this repository directly into your Unity project's `Assets` folder. Do not use the old `.unitypackage` releases as they are outdated.
3.  **Setup:** Add the `VehicleNoiseSynthesizer` component to your vehicle's audio root GameObject.

---

**_Can be used for:_**

:ballot_box_with_check: Engine

:ballot_box_with_check: Intake

:ballot_box_with_check: Exhausts

:ballot_box_with_check: Transmission⁰

:ballot_box_with_check: Differential⁰ *(and alike)\_

:information_source: Uses real audio clips per Engine RPM and Engine Load to create realistic sound/noise. Two-neighbour constant-power crossfade with cylinder-aware pair hold timing.

---

**_Pros:_** _Some of the main reasons for devising this asset:_

:white_check_mark: Lightweight - async Burst-compiled calculations per fixed delta time (non-WebGL), synchronous fallback for WebGL.

:white_check_mark: Uses only Unity, optionally supports Unity Audio Mixer. Full WebGL compatibility.

:white_check_mark: Advanced parameters to fine-tune the audio effect - per-clip min/max pitch, volume offset, pitch offset.

:white_check_mark: Accurate constant-power crossfade blending between neighbouring RPM clips (cos² + sin² = 1).

:white_check_mark: Refreshed custom inspector with real-time volume envelope visualisation.

:white_check_mark: Cylinder-aware pair hold timing for realistic combustion-engine character.

---

**_Cons:_**

:x: Needs separate audio files per RPM¹

:x: Needs careful tweaking and spending time adjusting parameters to properly simulate a realistic vehicle sound with it

---

🔁 **_How to loop?_**

These are useful to compose loop audio for your vehicle sound simulation.

- Here it is described how to seamlessly loop audio using a free audio tool manually:
  - Intermediate Approach: https://gamedevbeginner.com/create-looping-sound-effects-for-games-for-free-with-audacity/
  - Advanced Approach: https://youtu.be/lMZXjCeAUPM
- This one is even much easier, you don't need to download a tool, and is online but a bit more limited: https://www.drumbot.com/projects/looper/
- This uses paid software to correct the pitch and make a cleaner seamless audio clip: https://youtu.be/1bnasSQbBqk

---

:warning: **_Alternatives?_**

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

Note that with DAW or standalone apps and plugins you may record looped audio clips for this (or other assets) for simulating your audio in real-time.

---

ℹ️ **_How to use it?**

Either download the UnityPackage or a zipped archive of this repo and import into a new Unity project. This asset has demo scenes that need NWH Vehicle Physics and NWH Dynamic Water Physics. To use any demo scene you will need those assets imported first. You may have none, one, or all of the NWH assets - just delete the scripts that are not relevant to your project (e.g. NWH Input demo samples if no NWH Asset is used).

1. Add the `VehicleNoiseSynthesizer` component to a GameObject
2. Assign an AudioSource Template and populate the Acceleration (and optionally Deceleration) clip banks
3. Set per-clip RPM values and min/max pitch ranges
4. Use the new inspector visualisation to verify your blend curves
5. Feed RPM and Load values via script (see `AudioGranulatorSimpleUI` for a test harness, or the NWH adapters for real vehicle integration)

---

ℹ️ **_How does it work?**

<img src="https://raw.githubusercontent.com/ATG-Simulator/VehicleNoiseSynthesizer/main/Depiction.jpg" alt="How does this asset work, Simplified in an image." width="65%">

Two neighbouring audio clips (lo / hi) are crossfaded at any given RPM using a constant-power cosine/sine pan law (`cos² + sin² = 1`). The active pair is selected by a hysteresis-buffered nearest-neighbour search with cylinder-aware hold timing. Per-clip pitch follows a continuous RPM-progress mapping. A Burst-compiled job system (non-WebGL) batches all synthesizer instances for maximum performance.

---

ℹ️ **_Why the heck am I sharing this freely?**

I expect people to happily use this but also improve it and share an enhanced version with others. _Happy coding! :)_

---

:copyright: Dan. The original script and its idea were inspired by several internet sources and GitHub Repositories like Keijiro Takahashi, CombatWombatZockchster, manueleisl, and others. The original idea was published as a GitHub repo to recreate a Forza Motorsport 4 type of audio simulation for cars - which unfortunately I lost its URL².

Thanks for your attention.

_Check the social media of my passion project called ATG Simulator please:_

- <https://twitter.com/atg_simulator>
- <https://www.instagram.com/atg_simulator/>
- <https://atg-simulator.com/>
- <https://youtube.com/@atg-simulator>
- <https://youtube.com/c/imdanoush>

---

**Footnotes:**

⁰ Transmission and differential sounds from the video are not by this script. Also blow-off and other sounds are not part of this asset as they are not intended to be.

¹ This script is also inspired by this video of the Turn10 Audio Engineer: <https://youtu.be/UNvka9GL-9k>. Same as Forza Horizon 3 or Forza Motorsport 7, it needs audio clips based on different rpm speeds. _E.g. `Ferrari458Engine_Accelerating_at_the_rpm_speed_of_5000.wav`._ It needs at least one accelerating audio clip; very few or too many clips may result in subpar quality.

² In case you know the repository, feel free to let everyone know by adding a comment in the main GitHub repository.
