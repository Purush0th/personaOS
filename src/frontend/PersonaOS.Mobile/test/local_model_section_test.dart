import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:personaos_mobile/api/personaos_api.dart';
import 'package:personaos_mobile/screens/local_model_section.dart';

/// The recommended local model on the phone: found through the server, downloaded only when
/// asked, then offered for the provider fields.
void main() {
  const model = 'hf.co/purush0th/personaos-qwen3-4b-instruct-GGUF:Q4_K_M';

  Map<String, Object?> status({required bool installed}) => {
    'found': true,
    'address': 'http://ollama:11434',
    'version': '0.40.15',
    'model': model,
    'displayName': 'PersonaOS model (Qwen3 4B, fine-tuned)',
    'sizeBytes': 2497280160,
    'license': 'Apache-2.0',
    'licenseUrl': 'https://www.apache.org/licenses/LICENSE-2.0',
    'notice': 'It can be wrong.',
    'installed': installed,
    'message': installed ? 'Ollama already has the model.' : 'Ollama 0.40.15 found at http://ollama:11434.',
  };

  testWidgets('finds Ollama, downloads on request, then offers the model', (tester) async {
    var installed = false;
    final requests = <String>[];
    final server = MockClient((request) async {
      requests.add('${request.method} ${request.url.path}');
      if (request.url.path == '/api/setup/local-model/pull') {
        expect(jsonDecode(request.body), {'address': 'http://ollama:11434'});
        installed = true;
        return http.Response(
          '{"status":"pulling 1a","completed":50,"total":100}\n{"status":"success"}\n{"done":true}\n',
          200,
        );
      }
      return http.Response(
        jsonEncode(status(installed: installed)),
        200,
        headers: {'content-type': 'application/json'},
      );
    });

    String? chosen;
    await http.runWithClient(() async {
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: LocalModelSection(
              api: PersonaOsApi(serverUrl: 'http://test'),
              address: () => '',
              onChosen: (address, m) => chosen = '$address $m',
            ),
          ),
        ),
      );

      await tester.tap(find.text('Find Ollama'));
      await tester.pumpAndSettle();
      expect(find.textContaining('2.5 GB download'), findsOneWidget);
      expect(requests, ['GET /api/setup/local-model'], reason: 'nothing downloads before Download is pressed');

      await tester.tap(find.text('Download'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Use this model'));
    }, () => server);

    expect(requests, contains('POST /api/setup/local-model/pull'));
    expect(chosen, 'http://ollama:11434 $model');
  });

  test('a download line knows how far it is', () {
    expect(ModelPullLine.fromJson({'status': 'pulling', 'completed': 25, 'total': 100}).fraction, 0.25);
    expect(ModelPullLine.fromJson({'status': 'pulling manifest'}).fraction, isNull);
    expect(ModelPullLine.fromJson({'error': 'no space'}).error, 'no space');
  });
}
