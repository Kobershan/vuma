import 'dart:convert';
import 'dart:io';
import 'package:flutter/material.dart';

const apiBaseUrl = String.fromEnvironment(
  'VUMA_API_BASE_URL',
  defaultValue: 'https://localhost:7243/api/v1',
);

void main() => runApp(VumaApp(api: VumaApi()));

class VumaApp extends StatelessWidget {
  final VumaApi api;
  const VumaApp({super.key, required this.api});
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
    home: AuthGate(api: api),
  );
}

class VumaApi {
  String? accessToken;

  Future<void> signIn(String userName, String password, String? storeId) async {
    final result = await _request(
      '/auth/token',
      method: 'POST',
      body: <String, dynamic>{
        'userName': userName,
        'password': password,
        if (storeId != null && storeId.trim().isNotEmpty)
          'storeId': storeId.trim(),
      },
    );
    accessToken = result['accessToken'] as String?;
    if (accessToken == null || accessToken!.isEmpty) {
      throw const FormatException('The API did not return an access token.');
    }
  }

  Future<Map<String, dynamic>> get(String path) async {
    return _request(path);
  }

  Future<Map<String, dynamic>> _request(
    String path, {
    String method = 'GET',
    Map<String, dynamic>? body,
  }) async {
    final client = HttpClient();
    try {
      final request = await client.openUrl(
        method,
        Uri.parse('$apiBaseUrl$path'),
      );
      request.headers.set(HttpHeaders.acceptHeader, 'application/json');
      if (accessToken != null) {
        request.headers.set(
          HttpHeaders.authorizationHeader,
          'Bearer $accessToken',
        );
      }
      if (body != null) {
        request.headers.contentType = ContentType.json;
        request.write(jsonEncode(body));
      }
      final response = await request.close();
      final responseBody = await response.transform(utf8.decoder).join();
      if (response.statusCode >= 400) {
        throw HttpException('API ${response.statusCode}: $responseBody');
      }
      return jsonDecode(responseBody) as Map<String, dynamic>;
    } finally {
      client.close(force: true);
    }
  }
}

class AuthGate extends StatefulWidget {
  final VumaApi api;
  const AuthGate({super.key, required this.api});
  @override
  State<AuthGate> createState() => _AuthGateState();
}

class _AuthGateState extends State<AuthGate> {
  bool signedIn = false;

  @override
  Widget build(BuildContext context) => signedIn
      ? OperationsShell(api: widget.api)
      : LoginPage(
          api: widget.api,
          onSignedIn: () => setState(() => signedIn = true),
        );
}

class LoginPage extends StatefulWidget {
  final VumaApi api;
  final VoidCallback onSignedIn;
  const LoginPage({super.key, required this.api, required this.onSignedIn});
  @override
  State<LoginPage> createState() => _LoginPageState();
}

class _LoginPageState extends State<LoginPage> {
  final userName = TextEditingController();
  final password = TextEditingController();
  final storeId = TextEditingController();
  bool loading = false;
  String? error;

  @override
  void dispose() {
    userName.dispose();
    password.dispose();
    storeId.dispose();
    super.dispose();
  }

  Future<void> submit() async {
    setState(() {
      loading = true;
      error = null;
    });
    try {
      await widget.api.signIn(
        userName.text.trim(),
        password.text,
        storeId.text,
      );
      if (mounted) widget.onSignedIn();
    } catch (e) {
      if (mounted) setState(() => error = e.toString());
    } finally {
      if (mounted) setState(() => loading = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    body: Center(
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 420),
        child: Card(
          margin: const EdgeInsets.all(24),
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Text(
                  'Sign in to Vuma',
                  style: Theme.of(context).textTheme.headlineSmall,
                ),
                const SizedBox(height: 20),
                TextField(
                  controller: userName,
                  decoration: const InputDecoration(labelText: 'Username'),
                ),
                const SizedBox(height: 12),
                TextField(
                  controller: password,
                  obscureText: true,
                  decoration: const InputDecoration(labelText: 'Password'),
                ),
                const SizedBox(height: 12),
                TextField(
                  controller: storeId,
                  decoration: const InputDecoration(
                    labelText: 'Store ID (optional)',
                  ),
                ),
                if (error != null) ...[
                  const SizedBox(height: 12),
                  Text(
                    error!,
                    style: const TextStyle(color: Colors.orangeAccent),
                  ),
                ],
                const SizedBox(height: 20),
                FilledButton(
                  onPressed: loading ? null : submit,
                  child: Text(loading ? 'Signing in…' : 'Sign in'),
                ),
              ],
            ),
          ),
        ),
      ),
    ),
  );
}

class OperationsShell extends StatefulWidget {
  final VumaApi api;
  const OperationsShell({super.key, required this.api});
  @override
  State<OperationsShell> createState() => _OperationsShellState();
}

class _OperationsShellState extends State<OperationsShell> {
  int index = 0;
  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, size) {
      final wide = size.maxWidth >= 900;
      final pages = <Widget>[
        OverviewPage(api: widget.api),
        const TransfersPage(),
        const OrdersPage(),
        const SettingsPage(),
      ];
      return Scaffold(
        appBar: wide
            ? null
            : AppBar(
                title: const Text('Vuma'),
                actions: [
                  IconButton(
                    onPressed: () => setState(() {}),
                    icon: const Icon(Icons.notifications_none),
                  ),
                ],
              ),
        body: Row(
          children: [
            if (wide)
              Container(
                width: 230,
                color: const Color(0xff0e0e0c),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    const Padding(
                      padding: EdgeInsets.fromLTRB(24, 30, 16, 34),
                      child: Text(
                        'Vuma\nGROUP CONTROL',
                        style: TextStyle(
                          fontSize: 22,
                          fontWeight: FontWeight.w700,
                          color: Color(0xfff0ac4c),
                          height: 1.15,
                        ),
                      ),
                    ),
                    const Padding(
                      padding: EdgeInsets.symmetric(
                        horizontal: 24,
                        vertical: 8,
                      ),
                      child: Text(
                        'OPERATE',
                        style: TextStyle(fontSize: 11, color: Colors.white38),
                      ),
                    ),
                    _SideItem(
                      icon: Icons.grid_view_outlined,
                      label: 'Overview',
                      active: index == 0,
                      onTap: () => setState(() => index = 0),
                    ),
                    _SideItem(
                      icon: Icons.point_of_sale_outlined,
                      label: 'Sales',
                      active: false,
                      onTap: () => setState(() => index = 0),
                    ),
                    _SideItem(
                      icon: Icons.inventory_2_outlined,
                      label: 'Inventory',
                      active: false,
                      onTap: () => setState(() => index = 0),
                    ),
                    _SideItem(
                      icon: Icons.swap_horiz,
                      label: 'Transfers',
                      active: index == 1,
                      onTap: () => setState(() => index = 1),
                    ),
                    const Padding(
                      padding: EdgeInsets.fromLTRB(24, 18, 16, 8),
                      child: Text(
                        'GROW',
                        style: TextStyle(fontSize: 11, color: Colors.white38),
                      ),
                    ),
                    _SideItem(
                      icon: Icons.shopping_bag_outlined,
                      label: 'Orders',
                      active: index == 2,
                      onTap: () => setState(() => index = 2),
                    ),
                    _SideItem(
                      icon: Icons.favorite_border,
                      label: 'Loyalty',
                      active: false,
                      onTap: () => setState(() => index = 0),
                    ),
                    const Padding(
                      padding: EdgeInsets.fromLTRB(24, 18, 16, 8),
                      child: Text(
                        'SYSTEM',
                        style: TextStyle(fontSize: 11, color: Colors.white38),
                      ),
                    ),
                    _SideItem(
                      icon: Icons.settings_outlined,
                      label: 'Settings',
                      active: index == 3,
                      onTap: () => setState(() => index = 3),
                    ),
                    const Spacer(),
                    const Padding(
                      padding: EdgeInsets.all(20),
                      child: Text(
                        '●  24 / 25 stores online',
                        style: TextStyle(
                          color: Color(0xff57c4a5),
                          fontSize: 12,
                        ),
                      ),
                    ),
                  ],
                ),
              ),
            Expanded(child: pages[index]),
          ],
        ),
        bottomNavigationBar: wide
            ? null
            : NavigationBar(
                selectedIndex: index,
                onDestinationSelected: (v) => setState(() => index = v),
                destinations: const [
                  NavigationDestination(
                    icon: Icon(Icons.grid_view_outlined),
                    selectedIcon: Icon(Icons.grid_view),
                    label: 'Home',
                  ),
                  NavigationDestination(
                    icon: Icon(Icons.swap_horiz),
                    label: 'Transfers',
                  ),
                  NavigationDestination(
                    icon: Icon(Icons.shopping_bag_outlined),
                    label: 'Orders',
                  ),
                  NavigationDestination(
                    icon: Icon(Icons.more_horiz),
                    label: 'More',
                  ),
                ],
              ),
      );
    },
  );
}

class _SideItem extends StatelessWidget {
  final IconData icon;
  final String label;
  final bool active;
  final VoidCallback onTap;
  const _SideItem({
    required this.icon,
    required this.label,
    required this.active,
    required this.onTap,
  });
  @override
  Widget build(BuildContext context) => ListTile(
    leading: Icon(
      icon,
      color: active ? const Color(0xfff0ac4c) : Colors.white54,
    ),
    title: Text(
      label,
      style: TextStyle(color: active ? Colors.white : Colors.white60),
    ),
    selected: active,
    onTap: onTap,
  );
}

class OverviewPage extends StatelessWidget {
  final VumaApi api;
  const OverviewPage({super.key, required this.api});
  @override
  Widget build(BuildContext context) => RefreshIndicator(
    onRefresh: () async => (context as Element).markNeedsBuild(),
    child: ListView(
      padding: const EdgeInsets.all(24),
      children: [
        Row(
          children: [
            const CircleAvatar(
              backgroundColor: Color(0xff40301c),
              child: Text('KP', style: TextStyle(color: Color(0xfff0ac4c))),
            ),
            const SizedBox(width: 12),
            const Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    'Good morning, Kobershan',
                    style: TextStyle(fontWeight: FontWeight.w600),
                  ),
                  Text(
                    'Group view · 25 stores linked',
                    style: TextStyle(color: Colors.white54, fontSize: 12),
                  ),
                ],
              ),
            ),
            IconButton(
              onPressed: () {},
              icon: const Icon(Icons.notifications_none),
            ),
          ],
        ),
        const SizedBox(height: 20),
        _HeroRevenue(api: api),
        const SizedBox(height: 14),
        SingleChildScrollView(
          scrollDirection: Axis.horizontal,
          child: Row(
            children: const [
              _Kpi(
                label: 'Stock on hand',
                value: 'R 9.6M',
                icon: Icons.inventory_2_outlined,
                color: Color(0xfff0ac4c),
              ),
              _Kpi(
                label: 'Transfers in motion',
                value: '17',
                icon: Icons.swap_horiz,
                color: Color(0xff9db0cc),
              ),
              _Kpi(
                label: 'Points redeemed',
                value: '48,120',
                icon: Icons.favorite_border,
                color: Color(0xff57c4a5),
              ),
              _Kpi(
                label: 'Open orders',
                value: '94',
                icon: Icons.shopping_bag_outlined,
                color: Color(0xffe07c63),
              ),
            ],
          ),
        ),
        const SizedBox(height: 24),
        const Text(
          'Quick actions',
          style: TextStyle(fontSize: 18, fontWeight: FontWeight.w600),
        ),
        const SizedBox(height: 12),
        GridView.count(
          shrinkWrap: true,
          physics: const NeverScrollableScrollPhysics(),
          crossAxisCount: 2,
          childAspectRatio: 2.5,
          crossAxisSpacing: 10,
          mainAxisSpacing: 10,
          children: const [
            _Action(icon: Icons.swap_horiz, label: 'New transfer'),
            _Action(icon: Icons.qr_code_scanner, label: 'Scan stock'),
            _Action(icon: Icons.shopping_bag_outlined, label: 'Orders'),
            _Action(icon: Icons.search, label: 'Loyalty lookup'),
          ],
        ),
        const SizedBox(height: 24),
        const Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Text(
              'Recent activity',
              style: TextStyle(fontSize: 18, fontWeight: FontWeight.w600),
            ),
            Text('See all', style: TextStyle(color: Color(0xfff0ac4c))),
          ],
        ),
        const SizedBox(height: 8),
        const Card(
          child: Column(
            children: [
              _Activity(
                icon: Icons.swap_horiz,
                title: 'Transfer approved',
                detail: 'Pinetown → Phoenix · Cooking oil',
                time: '08:12',
              ),
              _Activity(
                icon: Icons.favorite_border,
                title: 'Gold tier reached',
                detail: '142 members crossed threshold',
                time: '07:48',
              ),
              _Activity(
                icon: Icons.shopping_bag_outlined,
                title: 'Order #VM-38821 delivered',
                detail: 'Parklane Spar · POD confirmed',
                time: '07:20',
              ),
            ],
          ),
        ),
      ],
    ),
  );
}

class _HeroRevenue extends StatelessWidget {
  final VumaApi api;
  const _HeroRevenue({required this.api});
  @override
  Widget build(BuildContext context) => Card(
    color: const Color(0xff211b12),
    child: Padding(
      padding: const EdgeInsets.all(18),
      child: FutureBuilder<Map<String, dynamic>>(
        future: api.get('/dashboard/overview'),
        builder: (context, snap) {
          final data = snap.data;
          return Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Text(
                'Group revenue, today',
                style: TextStyle(color: Colors.white60),
              ),
              const SizedBox(height: 8),
              Text(
                data == null ? 'R —' : 'R ${data['salesToday'] ?? 0}',
                style: const TextStyle(
                  fontSize: 30,
                  fontWeight: FontWeight.w700,
                  color: Color(0xfff3f0e7),
                ),
              ),
              const SizedBox(height: 5),
              const Text(
                '▲ 4.2%',
                style: TextStyle(
                  color: Color(0xff57c4a5),
                  fontWeight: FontWeight.w600,
                ),
              ),
              const SizedBox(height: 8),
              const LinearProgressIndicator(
                value: .72,
                color: Color(0xfff0ac4c),
                backgroundColor: Color(0xff40301c),
              ),
            ],
          );
        },
      ),
    ),
  );
}

class _Kpi extends StatelessWidget {
  final String label, value;
  final IconData icon;
  final Color color;
  const _Kpi({
    required this.label,
    required this.value,
    required this.icon,
    required this.color,
  });
  @override
  Widget build(BuildContext context) => SizedBox(
    width: 154,
    child: Card(
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(icon, color: color),
            const SizedBox(height: 8),
            Text(
              value,
              style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w700),
            ),
            Text(
              label,
              style: const TextStyle(color: Colors.white54, fontSize: 11),
            ),
          ],
        ),
      ),
    ),
  );
}

class _Action extends StatelessWidget {
  final IconData icon;
  final String label;
  const _Action({required this.icon, required this.label});
  @override
  Widget build(BuildContext context) => Card(
    child: InkWell(
      onTap: () {},
      child: Row(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(icon, color: const Color(0xfff0ac4c)),
          const SizedBox(width: 8),
          Text(label, style: const TextStyle(fontSize: 12)),
        ],
      ),
    ),
  );
}

class _Activity extends StatelessWidget {
  final IconData icon;
  final String title, detail, time;
  const _Activity({
    required this.icon,
    required this.title,
    required this.detail,
    required this.time,
  });
  @override
  Widget build(BuildContext context) => ListTile(
    leading: Icon(icon, color: const Color(0xfff0ac4c)),
    title: Text(title, style: const TextStyle(fontSize: 13)),
    subtitle: Text(detail, style: const TextStyle(fontSize: 11)),
    trailing: Text(
      time,
      style: const TextStyle(color: Colors.white38, fontSize: 11),
    ),
  );
}

class TransfersPage extends StatelessWidget {
  const TransfersPage({super.key});
  @override
  Widget build(BuildContext context) => _ListPage(
    title: 'Transfers',
    child: Column(
      children: const [
        _Transfer(
          route: 'Regional KZN → Parklane',
          item: 'Maize meal, 10kg × 80',
          value: 'R 6,240',
          status: 'Awaiting store',
        ),
        _Transfer(
          route: 'Pinetown → Phoenix',
          item: 'Cooking oil, 2L × 150',
          value: 'R 18,900',
          status: 'Escalated',
        ),
        _Transfer(
          route: 'Parklane → Chatsworth',
          item: 'Sugar, 2.5kg × 90',
          value: 'R 4,050',
          status: 'Awaiting store',
        ),
        _Transfer(
          route: 'Head office → Musgrave',
          item: 'Rice, 5kg × 200',
          value: 'R 11,400',
          status: 'Approved',
        ),
      ],
    ),
  );
}

class _Transfer extends StatelessWidget {
  final String route, item, value, status;
  const _Transfer({
    required this.route,
    required this.item,
    required this.value,
    required this.status,
  });
  @override
  Widget build(BuildContext context) => Card(
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text(route, style: const TextStyle(fontWeight: FontWeight.w600)),
              Text(
                status,
                style: TextStyle(
                  color: status == 'Approved'
                      ? const Color(0xff57c4a5)
                      : const Color(0xfff0ac4c),
                  fontSize: 11,
                ),
              ),
            ],
          ),
          const SizedBox(height: 10),
          Text(item, style: const TextStyle(color: Colors.white60)),
          const SizedBox(height: 4),
          Text(value, style: const TextStyle(fontWeight: FontWeight.w700)),
          if (status != 'Approved')
            Row(
              mainAxisAlignment: MainAxisAlignment.end,
              children: [
                TextButton(onPressed: () {}, child: const Text('Decline')),
                FilledButton(onPressed: () {}, child: const Text('Approve')),
              ],
            ),
        ],
      ),
    ),
  );
}

class OrdersPage extends StatelessWidget {
  const OrdersPage({super.key});
  @override
  Widget build(BuildContext context) => _ListPage(
    title: 'Orders',
    child: Column(
      children: const [
        _Order(
          id: '#VM-38821',
          store: 'Parklane Spar · N. Chetty',
          amount: 'R 640',
          progress: .72,
        ),
        _Order(
          id: '#VM-38819',
          store: 'Pinetown C&C · T. Naidoo',
          amount: 'R 1,210',
          progress: 1,
        ),
        _Order(
          id: '#VM-38817',
          store: 'Phoenix C&C · S. Govender',
          amount: 'R 385',
          progress: .2,
        ),
        _Order(
          id: '#VM-38814',
          store: 'Chatsworth Spar · R. Pillay',
          amount: 'R 902',
          progress: 1,
        ),
      ],
    ),
  );
}

class _Order extends StatelessWidget {
  final String id, store, amount;
  final double progress;
  const _Order({
    required this.id,
    required this.store,
    required this.amount,
    required this.progress,
  });
  @override
  Widget build(BuildContext context) => Card(
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        children: [
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(id, style: const TextStyle(fontWeight: FontWeight.w600)),
                  Text(
                    store,
                    style: const TextStyle(color: Colors.white54, fontSize: 11),
                  ),
                ],
              ),
              Text(amount, style: const TextStyle(fontWeight: FontWeight.w700)),
            ],
          ),
          const SizedBox(height: 14),
          LinearProgressIndicator(
            value: progress,
            color: const Color(0xff57c4a5),
            backgroundColor: Colors.white12,
          ),
          const SizedBox(height: 5),
          const Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text(
                'Processing',
                style: TextStyle(fontSize: 10, color: Colors.white54),
              ),
              Text(
                'Transit',
                style: TextStyle(fontSize: 10, color: Colors.white54),
              ),
              Text(
                'Delivered',
                style: TextStyle(fontSize: 10, color: Colors.white54),
              ),
            ],
          ),
        ],
      ),
    ),
  );
}

class _ListPage extends StatelessWidget {
  final String title;
  final Widget child;
  const _ListPage({required this.title, required this.child});
  @override
  Widget build(BuildContext context) => ListView(
    padding: const EdgeInsets.all(24),
    children: [
      Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Text(
            title,
            style: const TextStyle(fontSize: 28, fontWeight: FontWeight.w700),
          ),
          IconButton(onPressed: () {}, icon: const Icon(Icons.tune)),
        ],
      ),
      const SizedBox(height: 16),
      if (title == 'Orders')
        const Card(
          child: ListTile(
            leading: Icon(Icons.search),
            title: Text(
              'Search order, customer, store…',
              style: TextStyle(color: Colors.white54),
            ),
          ),
        ),
      const SizedBox(height: 8),
      child,
    ],
  );
}

class SettingsPage extends StatelessWidget {
  const SettingsPage({super.key});
  @override
  Widget build(BuildContext context) => _ListPage(
    title: 'Settings',
    child: Column(
      children: const [
        Card(
          child: ListTile(
            leading: Icon(Icons.cloud_outlined, color: Color(0xff57c4a5)),
            title: Text('Cloud connection'),
            subtitle: Text(apiBaseUrl),
          ),
        ),
        Card(
          child: ListTile(
            leading: Icon(Icons.notifications_none),
            title: Text('WhatsApp notifications'),
            subtitle: Text('Twilio WhatsApp is configured on the server.'),
          ),
        ),
      ],
    ),
  );
}
