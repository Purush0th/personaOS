import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:personaos_mobile/api/personaos_api.dart';

/// A wrong password must read as one, whatever body the server or a proxy sends with the 401.
void main() {
  for (final body in ['{"error":"Incorrect username or password."}', '', '<html>401</html>']) {
    test('a 401 with body "$body" says the password is wrong', () async {
      final error = await http.runWithClient(
        () => PersonaOsApi(serverUrl: 'http://test').login('purush', 'wrong'),
        () => MockClient((_) async => http.Response(body, 401)),
      );
      expect(error, 'Incorrect username or password.');
    });
  }
}
