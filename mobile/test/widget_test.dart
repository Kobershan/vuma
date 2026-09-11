import 'package:flutter_test/flutter_test.dart';
import 'package:vuma_client/main.dart';

void main() {
  testWidgets('Vuma shell renders its navigation', (tester) async {
    await tester.pumpWidget(const VumaApp());
    expect(find.text('Vuma Retail'), findsOneWidget);
    expect(find.text('Good morning'), findsOneWidget);
  });
}
