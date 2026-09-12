import 'package:flutter_test/flutter_test.dart';
import 'package:vuma_client/main.dart';

void main() {
  testWidgets('Vuma shell renders its navigation', (tester) async {
    await tester.pumpWidget(VumaApp(api: VumaApi()));
    expect(find.text('Sign in to Vuma'), findsOneWidget);
  });
}
