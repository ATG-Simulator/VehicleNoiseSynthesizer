# Vehicle Noise Synthesizer
 VNS is an open-source free audio addon primarily designed for Unity to simulate vehicle sounds based on engine rpm similar to a granulator without dependencies such as FMOD.

[![Watch the video](https://cdn.discordapp.com/attachments/705004606425661552/1139956388550950922/image.png)](https://cdn.discordapp.com/attachments/890033761394241556/1139954515701936188/VNSNWHv1.0.mp4)

[Demo 1 - Car](https://cdn.discordapp.com/attachments/1106252572521676890/1120682622973136996/VID_20230620_135154_906.mp4)

[Demo 2 - Boat](https://cdn.discordapp.com/attachments/705004655394160740/1127147410230100038/NWHDWP2AudioSimTest.mp4)

```
An Enhanced FM4-Inspired Vehicle Sound Synthesizer
```

***Can be used for:***

:ballot_box_with_check: Engine

:ballot_box_with_check: Intake

:ballot_box_with_check: Exhausts

:ballot_box_with_check: Transmission⁰

:ballot_box_with_check: Differential⁰ _(and alike)_

:information_source:  This uses real audio clips per Engine RPM and Engine Load to create realistic sound/noise



***Pros:*** _Some of the main reasons for devising this asset:_

:white_check_mark:  Lightweight (Async Calculations - per fixed delta time)

:white_check_mark:  Uses only Unity, Optionally supports Unity Audio Mixer. Thus it's possible to build WebGL projects.

:white_check_mark:  Auto setups audio clips and procedural Audio Source creation make it user-friendly for modding



***Cons:***

:x: Needs separate audio files per RPM¹

:x: Does not respect engine piston cycle frequency for fading audio clips *Or in other words, it fades audio clips linearly smoothly which is not "always" good*

🔁***How to loop?***

These are useful to compose loop audio for your vehicle sound simulation.
*  Here it is described how to seamlessly loop audio using a free audio tool manually:
   * Intermediate Approach:  https://gamedevbeginner.com/create-looping-sound-effects-for-games-for-free-with-audacity/
   * Advanced Approach: https://youtu.be/lMZXjCeAUPM
*  This one is even much easier, you don't need to download a tool, and is online but a bit more limited: https://www.drumbot.com/projects/looper/
*  This uses paid software to correct the pitch and make a cleaner seamless audio clip: https://youtu.be/1bnasSQbBqk

:warning:***Alternatives?***

Although this asset was done with the idea of using it in [my passion project ATG Simulator](https://ATG-Simulator.com), I would say the paid ones might be easier to work with, below is a list of assets and plugins that you can use instead of this one for your projects - the prices are valid at the time of writing this text:

 - [FMOD Indie _(Free under some limitations, Needs your audio clips)_](https://www.fmod.com/download)
 - Unity Asset : [**Realistic Engine Sounds** (from 23 US$ to +260 USD with most needed audio clips)](https://assetstore.unity.com/packages/tools/audio/realistic-engine-sounds-2-pro-edition-224783)
 - Unity/Unreal/... Plugin: [**Crankcase Rev** (from 1,500 USD)](https://www.audiokinetic.com/en/products/plugins/crankcase-rev/)
 - DAW Plugin _(Can mix both engine and exhaust sounds of different vehicles)_: [**Igniter** (430 USD)](https://www.krotosaudio.com/igniter/)
 - DAW Plugin _(Recommended for engine sounds only)_: [**Audio Motors**](https://lesound.io/product/audiomotors-pro/)
 - Unity Asset : [**Granular Synthesis for Engine Audio** (Free)](https://github.com/CombatWombatZockchster/Granular-Synthesis-for-Engine-Audio)
 - Unity/Unreal _(Procedural Engine Sounds)_: [**Nemisindo Engine** (44 USD)](https://assetstore.unity.com/packages/tools/audio/nemisindo-engine-procedural-sound-effects-222246) 
 - Standalone _(Procedural Realistic Car Sounds, Very CPU intensive)_: [**Engine Simulator** (Free)](https://www.engine-sim.parts/)
 - Android App _(Limited predefined audio files, Basic non-seamless loop)_: [**RevHeadz** (free but w/ in-app purchases)](https://rev-headz.com/)

Note that with DAW or standalone apps and plugins you may record looped audio clips for this (or the other assets) for simulating your audio in real-time.

:information_source: ***How to use it?***

Either download the unitypackage or a zipped archive of this repo. And import them into a new Unity project. Note that this asset has a pair of demo scenes that need NWH Vehicle Physics and NWH Dynamic Water Physics. To be able to use any of the two demo scenes you will need to import the said assets into your project first, then import this GitHub project. You shall have none of the NWH assets, one of them or all of them to use this asset. Just study the important codes which are commented line-by-line, And delete the scripts that are not related to your project  - e.g. NWH Input demo samples if no NWH Asset is used.

:information_source: ***How does it work?***

<img src="https://raw.githubusercontent.com/ATG-Simulator/VehicleNoiseSynthesizer/main/Depiction.jpg" alt="How does this asset work, Simplified in an image." width="65%">


:information_source: ***Why the heck am I sharing this freely?***

I expect people happily use this but also improve it and share an enhanced version of it with others. _Happy coding! :)_

:copyright: Dan. The original script and its idea were inspired by several internet sources and GitHub Repositories like Keijiro Takahashi, CombatWombatZockchster, manueleisl, and others. The original idea was published as a GitHub repo to recreate a Forza Motorsport 4 type of audio simulation for cars - which unfortunately I lost its URL².

Thanks for your attention.

_Check the social media of my passion project called ATG Simulator please:_
*  <https://twitter.com/atg_simulator>
*  <https://www.instagram.com/atg_simulator/>
*  <https://atg-simulator.com/>
*  <https://youtube.com/@atg-simulator>
*  <https://youtube.com/c/imdanoush>
_________________________________
Footnotes:

⁰ Transmission and differential sounds from the video are not by this script, Also blow-off and other sounds are not part of this asset as they are not intended to be.

¹ This script is also inspired by this video of the Turn10 Audio Engineer: <https://youtu.be/UNvka9GL-9k>. Same as Forza Horizon 3 or Forza Motorsport 7, it needs audio clips based on different rpm speeds. ```E.g Ferrari458Engine_Accelerating_at_the_rpm_speed_of_5000.wav``` and it needs at least one accelerating audio clip though very few and too many clips may result in subpar quality.

² In case you know the repository, feel free to let everyone know by adding a comment in the main GitHub repository.
