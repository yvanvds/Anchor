import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:go_router/go_router.dart';

import '../api/auth_token_store.dart';
import '../api/sessions_api.dart';
import '../realtime/session_hub_client.dart';

class SessionPage extends StatefulWidget {
  const SessionPage({
    super.key,
    required this.sessionId,
    required this.tokens,
    required this.sessions,
    required this.apiBaseUrl,
  });

  final String sessionId;
  final AuthTokenStore tokens;
  final SessionsApi sessions;
  final Uri apiBaseUrl;

  @override
  State<SessionPage> createState() => _SessionPageState();
}

class _SessionPageState extends State<SessionPage> {
  late final SessionHubClient _hub;
  StreamSubscription<SessionEvent>? _eventsSub;
  final List<SessionEvent> _events = [];
  bool _connecting = true;
  bool _ending = false;
  bool _ended = false;
  String? _error;
  SessionDetail? _detail;

  @override
  void initState() {
    super.initState();
    _hub = SessionHubClient(
      apiBaseUrl: widget.apiBaseUrl,
      tokenProvider: () async => widget.tokens.token,
    );
    _loadDetail();
    _connect();
  }

  Future<void> _loadDetail() async {
    try {
      final detail = await widget.sessions.getSession(widget.sessionId);
      if (!mounted) return;
      setState(() => _detail = detail);
    } catch (_) {
      // Non-fatal: the live event stream still works without the detail block.
      // The join-code panel just won't render.
    }
  }

  Future<void> _copyJoinCode(String code) async {
    await Clipboard.setData(ClipboardData(text: code));
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text('Copied $code'), duration: const Duration(seconds: 2)),
    );
  }

  Future<void> _connect() async {
    try {
      await _hub.connect();
      await _hub.joinSession(widget.sessionId);
      _eventsSub = _hub.events.listen((evt) {
        if (!mounted) return;
        setState(() {
          _events.insert(0, evt);
          if (evt.kind == 'SessionEnded' &&
              evt.payload['sessionId'] == widget.sessionId) {
            _ended = true;
          }
        });
      });
    } catch (e) {
      if (!mounted) return;
      setState(() => _error = 'Could not connect to live stream: $e');
    } finally {
      if (mounted) setState(() => _connecting = false);
    }
  }

  Future<void> _endSession() async {
    setState(() {
      _ending = true;
      _error = null;
    });
    try {
      await widget.sessions.endSession(widget.sessionId);
    } catch (e) {
      if (!mounted) return;
      setState(() => _error = 'Failed to end session: $e');
    } finally {
      if (mounted) setState(() => _ending = false);
    }
  }

  @override
  void dispose() {
    _eventsSub?.cancel();
    _hub.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text('Session ${widget.sessionId}'),
        leading: IconButton(
          icon: const Icon(Icons.arrow_back),
          onPressed: () => context.go('/'),
        ),
        actions: [
          if (!_ended)
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 8),
              child: TextButton.icon(
                onPressed: _ending ? null : _endSession,
                icon: _ending
                    ? const SizedBox(
                        width: 16,
                        height: 16,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(Icons.stop),
                label: const Text('End session'),
              ),
            ),
        ],
      ),
      body: Column(
        children: [
          if (_connecting) const LinearProgressIndicator(),
          if (_error != null)
            Container(
              width: double.infinity,
              color: Theme.of(context).colorScheme.errorContainer,
              padding: const EdgeInsets.all(12),
              child: Text(_error!),
            ),
          if (_detail != null && _detail!.joinCode.isNotEmpty && !_ended)
            _JoinCodePanel(
              code: _detail!.joinCode,
              onCopy: () => _copyJoinCode(_detail!.joinCode),
            ),
          if (_ended)
            Container(
              width: double.infinity,
              color: Theme.of(context).colorScheme.surfaceContainerHighest,
              padding: const EdgeInsets.all(12),
              child: const Text('Session ended — event stream stopped.'),
            ),
          Expanded(
            child: _events.isEmpty
                ? const Center(child: Text('Waiting for events…'))
                : ListView.separated(
                    padding: const EdgeInsets.all(12),
                    itemCount: _events.length,
                    separatorBuilder: (_, _) => const Divider(height: 8),
                    itemBuilder: (context, i) {
                      final evt = _events[i];
                      return ListTile(
                        dense: true,
                        title: Text(evt.kind),
                        subtitle: Text(evt.payload.toString()),
                        trailing: Text(
                          '${evt.at.hour.toString().padLeft(2, '0')}:'
                          '${evt.at.minute.toString().padLeft(2, '0')}:'
                          '${evt.at.second.toString().padLeft(2, '0')}',
                        ),
                      );
                    },
                  ),
          ),
        ],
      ),
    );
  }
}

class _JoinCodePanel extends StatelessWidget {
  const _JoinCodePanel({required this.code, required this.onCopy});

  final String code;
  final VoidCallback onCopy;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Container(
      width: double.infinity,
      color: theme.colorScheme.surfaceContainerHighest,
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'Join code',
                  style: theme.textTheme.labelMedium?.copyWith(
                    color: theme.colorScheme.onSurfaceVariant,
                  ),
                ),
                const SizedBox(height: 2),
                Text(
                  // Big enough to be legible across a classroom — fallback
                  // path for any student who didn't get the roster-based
                  // push (substitute, transferred class, etc).
                  code,
                  style: theme.textTheme.displaySmall?.copyWith(
                    fontFeatures: const [FontFeature.tabularFigures()],
                    fontWeight: FontWeight.w600,
                    letterSpacing: 8,
                  ),
                ),
              ],
            ),
          ),
          IconButton(
            tooltip: 'Copy code',
            icon: const Icon(Icons.copy),
            onPressed: onCopy,
          ),
        ],
      ),
    );
  }
}
