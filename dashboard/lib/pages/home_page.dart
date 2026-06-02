import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../api/auth_token_store.dart';
import '../api/sessions_api.dart';
import '../auth/msal_auth_service.dart';

class HomePage extends StatefulWidget {
  const HomePage({
    super.key,
    required this.tokens,
    required this.auth,
    required this.sessions,
  });

  final AuthTokenStore tokens;
  final MsalAuthService auth;
  final SessionsApi sessions;

  @override
  State<HomePage> createState() => _HomePageState();
}

class _HomePageState extends State<HomePage> {
  List<ClassSummary>? _classes;
  ClassSummary? _selected;
  bool _busy = false;
  String? _error;
  bool _isAdmin = false;

  @override
  void initState() {
    super.initState();
    _loadInitialData();
  }

  Future<void> _loadInitialData() async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      // Provision the user in the backend before any role-gated call —
      // /me upserts based on the Entra oid + role claim, idempotently.
      final me = await widget.sessions.me();
      final classes = await widget.sessions.classes();
      final department = widget.tokens.account?.department;
      ClassSummary? preferred;
      if (department != null && department.isNotEmpty) {
        for (final c in classes) {
          if (c.name == department) {
            preferred = c;
            break;
          }
        }
      }
      preferred ??= classes.isNotEmpty ? classes.first : null;

      if (!mounted) return;
      setState(() {
        _classes = classes;
        _selected = preferred;
        _isAdmin = me.isAdmin;
      });
    } catch (e) {
      if (!mounted) return;
      setState(() => _error = 'Could not load start-session data: $e');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _startSession() async {
    final klass = _selected;
    if (klass == null) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      // Sessions now start with no bundles — baseline-only enforcement. The
      // teacher adds bundles from the live session view (#93).
      final session = await widget.sessions.startSession(klass.id);
      if (!mounted) return;
      context.go('/session/${session.id}');
    } catch (e) {
      if (!mounted) return;
      setState(() => _error = 'Failed to start session: $e');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _signOut() async {
    try {
      await widget.auth.signOut();
    } finally {
      widget.tokens.clear();
      if (mounted) context.go('/login');
    }
  }

  @override
  Widget build(BuildContext context) {
    final account = widget.tokens.account;
    final classes = _classes;
    return Scaffold(
      appBar: AppBar(
        title: const Text('Anchor'),
        actions: [
          TextButton.icon(
            icon: const Icon(Icons.group),
            label: const Text('Manage classes'),
            onPressed: () => context.go('/classes'),
          ),
          TextButton.icon(
            icon: const Icon(Icons.history),
            label: const Text('Past sessions'),
            onPressed: () => context.go('/history'),
          ),
          if (_isAdmin)
            TextButton.icon(
              icon: const Icon(Icons.collections_bookmark),
              label: const Text('Bundles'),
              onPressed: () => context.go('/bundles'),
            ),
          if (account != null)
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 12),
              child: Center(child: Text(account.displayName)),
            ),
          IconButton(
            tooltip: 'Sign out',
            icon: const Icon(Icons.logout),
            onPressed: _signOut,
          ),
        ],
      ),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 480),
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                if (account?.department != null) ...[
                  Text(
                    'Your department: ${account!.department}',
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 16),
                ],
                if (classes == null && _busy)
                  const Center(child: CircularProgressIndicator())
                else if (classes == null || classes.isEmpty)
                  const Text(
                    'No classes assigned to you yet.',
                    textAlign: TextAlign.center,
                  )
                else ...[
                  DropdownButtonFormField<ClassSummary>(
                    initialValue: _selected,
                    decoration: const InputDecoration(labelText: 'Class'),
                    items: [
                      for (final c in classes)
                        DropdownMenuItem(
                          value: c,
                          child: Text('${c.name} (${c.schoolYear})'),
                        ),
                    ],
                    onChanged: _busy
                        ? null
                        : (value) => setState(() => _selected = value),
                  ),
                  const SizedBox(height: 24),
                  FilledButton.icon(
                    onPressed: _busy || _selected == null ? null : _startSession,
                    icon: _busy
                        ? const SizedBox(
                            width: 16,
                            height: 16,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Icon(Icons.play_arrow),
                    label: Text(
                      _selected == null
                          ? 'Select a class'
                          : 'Start session for class ${_selected!.name}',
                    ),
                  ),
                ],
                if (_error != null) ...[
                  const SizedBox(height: 16),
                  Text(
                    _error!,
                    style: TextStyle(color: Theme.of(context).colorScheme.error),
                    textAlign: TextAlign.center,
                  ),
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }
}
