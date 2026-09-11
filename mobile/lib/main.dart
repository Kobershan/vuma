import 'dart:convert';
import 'dart:io';
import 'package:flutter/material.dart';

const apiBaseUrl = String.fromEnvironment(
  'VUMA_API_BASE_URL',
  defaultValue: 'https://localhost:7243/api/v1',
);

void main() => runApp(const VumaApp());

class VumaApp extends StatelessWidget {
  const VumaApp({super.key});
  @override
  Widget build(BuildContext context) => MaterialApp(
    title: 'Vuma Retail',
    debugShowCheckedModeBanner: false,
    theme: ThemeData(
      brightness: Brightness.dark,
      colorScheme: ColorScheme.fromSeed(
        seedColor: const Color(0xff2dd477),
        brightness: Brightness.dark,
      ),
      scaffoldBackgroundColor: const Color(0xff0b1110),
      cardTheme: const CardThemeData(color: Color(0xff131c19)),
      useMaterial3: true,
    ),
    home: const OperationsShell(),
  );
}

class VumaApi {
  Future<Map<String, dynamic>> get(String path) async {
    final client = HttpClient();
    try {
      final request = await client.getUrl(Uri.parse('$apiBaseUrl$path'));
      request.headers.set(HttpHeaders.acceptHeader, 'application/json');
      final response = await request.close();
      final body = await response.transform(utf8.decoder).join();
      if (response.statusCode >= 400) {
        throw HttpException('API ${response.statusCode}: $body');
      }
      return jsonDecode(body) as Map<String, dynamic>;
    } finally {
      client.close(force: true);
    }
  }
}

class OperationsShell extends StatefulWidget {
  const OperationsShell({super.key});
  @override
  State<OperationsShell> createState() => _OperationsShellState();
}

class _OperationsShellState extends State<OperationsShell> {
  int index = 0;
  final pages = const [OverviewPage(), ShipmentsPage(), SettingsPage()];
  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, constraints) {
      final wide = constraints.maxWidth >= 900;
      return Scaffold(
        appBar: AppBar(
          title: const Text('Vuma Retail'),
          actions: [
            IconButton(
              onPressed: () => setState(() {}),
              icon: const Icon(Icons.refresh),
            ),
          ],
        ),
        drawer: wide
            ? null
            : NavigationDrawer(
                selectedIndex: index,
                onDestinationSelected: (v) {
                  setState(() => index = v);
                  Navigator.pop(context);
                },
                children: _drawer(),
              ),
        body: Row(
          children: [
            if (wide)
              NavigationRail(
                selectedIndex: index,
                onDestinationSelected: (v) => setState(() => index = v),
                labelType: NavigationRailLabelType.all,
                destinations: const [
                  NavigationRailDestination(
                    icon: Icon(Icons.grid_view_outlined),
                    selectedIcon: Icon(Icons.grid_view),
                    label: Text('Overview'),
                  ),
                  NavigationRailDestination(
                    icon: Icon(Icons.local_shipping_outlined),
                    selectedIcon: Icon(Icons.local_shipping),
                    label: Text('Shipments'),
                  ),
                  NavigationRailDestination(
                    icon: Icon(Icons.settings_outlined),
                    selectedIcon: Icon(Icons.settings),
                    label: Text('Settings'),
                  ),
                ],
              ),
            Expanded(child: pages[index]),
          ],
        ),
      );
    },
  );
  List<Widget> _drawer() => const [
    Padding(
      padding: EdgeInsets.fromLTRB(28, 20, 16, 10),
      child: Text('OPERATIONS'),
    ),
    NavigationDrawerDestination(
      icon: Icon(Icons.grid_view_outlined),
      selectedIcon: Icon(Icons.grid_view),
      label: Text('Overview'),
    ),
    NavigationDrawerDestination(
      icon: Icon(Icons.local_shipping_outlined),
      selectedIcon: Icon(Icons.local_shipping),
      label: Text('Shipments'),
    ),
    NavigationDrawerDestination(
      icon: Icon(Icons.settings_outlined),
      selectedIcon: Icon(Icons.settings),
      label: Text('Settings'),
    ),
  ];
}

class OverviewPage extends StatelessWidget {
  const OverviewPage({super.key});
  @override
  Widget build(BuildContext context) => ListView(
    padding: const EdgeInsets.all(24),
    children: [
      Text(
        'Good morning',
        style: Theme.of(
          context,
        ).textTheme.headlineMedium?.copyWith(fontWeight: FontWeight.w700),
      ),
      const SizedBox(height: 6),
      Text(
        'Your operations at a glance',
        style: Theme.of(
          context,
        ).textTheme.bodyLarge?.copyWith(color: Colors.white60),
      ),
      const SizedBox(height: 24),
      FutureBuilder<Map<String, dynamic>>(
        future: VumaApi().get('/dashboard/overview'),
        builder: (context, snapshot) {
          if (snapshot.hasError) {
            return _ErrorCard(message: snapshot.error.toString());
          }
          if (!snapshot.hasData) {
            return const Center(
              child: Padding(
                padding: EdgeInsets.all(40),
                child: CircularProgressIndicator(),
              ),
            );
          }
          final data = snapshot.data!;
          return Wrap(
            spacing: 14,
            runSpacing: 14,
            children: [
              _Metric(
                label: 'Sales today',
                value: 'R ${data['salesToday'] ?? 0}',
              ),
              _Metric(
                label: 'Orders today',
                value: '${data['ordersToday'] ?? 0}',
              ),
              _Metric(
                label: 'Open orders',
                value: '${data['openOrders'] ?? 0}',
              ),
            ],
          );
        },
      ),
      const SizedBox(height: 28),
      const Text(
        'Quick actions',
        style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700),
      ),
      const SizedBox(height: 12),
      Wrap(
        spacing: 12,
        runSpacing: 12,
        children: [
          ActionChip(
            avatar: const Icon(Icons.add_shopping_cart),
            label: const Text('New order'),
            onPressed: () {},
          ),
          ActionChip(
            avatar: const Icon(Icons.local_shipping),
            label: const Text('Track shipment'),
            onPressed: () {},
          ),
          ActionChip(
            avatar: const Icon(Icons.inventory_2),
            label: const Text('Check stock'),
            onPressed: () {},
          ),
        ],
      ),
    ],
  );
}

class ShipmentsPage extends StatelessWidget {
  const ShipmentsPage({super.key});
  @override
  Widget build(BuildContext context) => ListView(
    padding: const EdgeInsets.all(24),
    children: [
      Text(
        'Shipments',
        style: Theme.of(
          context,
        ).textTheme.headlineMedium?.copyWith(fontWeight: FontWeight.w700),
      ),
      const SizedBox(height: 8),
      const Text('Delivery execution, tracking and proof of delivery'),
      const SizedBox(height: 24),
      const Card(
        child: ListTile(
          leading: Icon(Icons.local_shipping, color: Color(0xff2dd477)),
          title: Text('Logistics API ready'),
          subtitle: Text('Manage runs and PODs through the Vuma API.'),
        ),
      ),
    ],
  );
}

class SettingsPage extends StatelessWidget {
  const SettingsPage({super.key});
  @override
  Widget build(BuildContext context) => ListView(
    padding: const EdgeInsets.all(24),
    children: [
      Text(
        'Settings',
        style: Theme.of(
          context,
        ).textTheme.headlineMedium?.copyWith(fontWeight: FontWeight.w700),
      ),
      const SizedBox(height: 24),
      const Card(
        child: ListTile(
          leading: Icon(Icons.cloud_outlined),
          title: Text('Cloud connection'),
          subtitle: Text(apiBaseUrl),
        ),
      ),
      const Card(
        child: ListTile(
          leading: Icon(Icons.notifications_none),
          title: Text('WhatsApp notifications'),
          subtitle: Text('Twilio WhatsApp is configured on the server.'),
        ),
      ),
    ],
  );
}

class _Metric extends StatelessWidget {
  final String label;
  final String value;
  const _Metric({required this.label, required this.value});
  @override
  Widget build(BuildContext context) => SizedBox(
    width: 220,
    child: Card(
      child: Padding(
        padding: const EdgeInsets.all(18),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(label, style: const TextStyle(color: Colors.white60)),
            const SizedBox(height: 10),
            Text(
              value,
              style: Theme.of(
                context,
              ).textTheme.headlineSmall?.copyWith(fontWeight: FontWeight.w700),
            ),
          ],
        ),
      ),
    ),
  );
}

class _ErrorCard extends StatelessWidget {
  final String message;
  const _ErrorCard({required this.message});
  @override
  Widget build(BuildContext context) => Card(
    child: Padding(
      padding: const EdgeInsets.all(18),
      child: Text(
        'Could not load live data. Check the API URL and sign-in.\n$message',
        style: const TextStyle(color: Colors.orangeAccent),
      ),
    ),
  );
}
