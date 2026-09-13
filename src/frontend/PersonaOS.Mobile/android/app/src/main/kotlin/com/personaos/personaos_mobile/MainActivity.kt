package com.personaos.personaos_mobile

import io.flutter.embedding.android.FlutterFragmentActivity

// FlutterFragmentActivity, not FlutterActivity: local_auth shows the biometric
// prompt as a fragment, and throws "no_fragment_activity" on a plain Activity.
class MainActivity : FlutterFragmentActivity()
