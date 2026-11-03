# ONNX Runtime (flutter_onnxruntime, the Silero speech detector) is reached through JNI: R8 must
# not strip or rename its classes in a release build.
-keep class ai.onnxruntime.** { *; }
