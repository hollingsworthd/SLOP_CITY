# Platform sequence

## 1. Mac — current delivery target

Compile an Apple Silicon `.app` from the Unity project and verify the first-person couch scene, controls, local objectives, and pause/quit flow. Keep the black visor curves removed. Add Developer ID signing and notarization for broader distribution; add and test an Intel/Universal Mac build if required.

The first release is a local lounge prototype. A persistent online city still needs authoritative servers, authentication, synchronized gameplay, moderation, asset streaming, and load testing. Client platform exports do not supply those systems automatically.

## 2. iOS and Android

Keep the same scene and game logic. Ship touch controls after device testing, then reduce draw calls and geometry where measurements require it: material sharing, instancing, LODs, texture atlases, lighting budgets, and measured memory limits. Add safe-area and pause/resume testing across screen shapes. Check thermal behavior, low-end devices, and sustained frame rate before choosing device minimums.

For iOS, add Unity iOS Build Support and export an Xcode project. This Mac already has Xcode. Device signing and distribution must be configured for Daniel's Apple account; App Store distribution requires Apple Developer membership. [Unity iOS setup](https://docs.unity3d.com/6000.3/Documentation/Manual/ios-environment-setup.html)

For Android, install the matching Android Build Support, SDK, NDK, and OpenJDK through Unity Hub. Produce a development APK for local device tests, then configure the release keystore and Android App Bundle for store distribution. [Unity Android dependencies](https://docs.unity3d.com/6000.3/Documentation/Manual/android-sdksetup.html)

Neither store submission nor a paid membership is authorized or performed by setting up this project.

## 3. Windows and Linux

Add the supported build modules, controller input/rebinding, and desktop-specific settings. Compile Windows and Linux clients and test each on real target hardware. Verify filesystem paths, graphics backends, display modes, cursor locking, and native plugin availability. Keep shared server protocols independent of the client platform.

## 4. PS5, Xbox, and Nintendo Switch

Pursue platform-holder developer approval and hardware access before scheduling ports. Console SDKs and Unity platform modules are gated; Unity Pro or a qualifying platform license is required. Unity's console build workflows require a Windows development environment. Console versions also need platform services, controller-only menus, suspend/resume behavior, performance tuning, and certification work. The current Mac project does not include or imitate restricted console SDKs. [Unity console access](https://unity.com/solutions/console), [Unity system requirements](https://docs.unity3d.com/6000.3/Documentation/Manual/system-requirements.html)

This is an ordered engineering plan, not a promise that changing a build target produces a finished port.
