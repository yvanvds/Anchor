import 'dart:convert';
import 'dart:js_interop';
import 'dart:js_interop_unsafe';

import 'bundle_prefs.dart';

@JS()
external JSObject get window;

class BundlePrefsImpl implements BundlePrefs {
  BundlePrefsImpl();

  static const String _selectionKeyPrefix = 'anchor.bundles.selection.';
  static const String _modeKeyPrefix = 'anchor.session.mode.';

  JSObject? get _storage {
    final raw = window['localStorage'];
    if (raw == null || !raw.isA<JSObject>()) return null;
    return raw as JSObject;
  }

  String _selectionKey(String accountKey) => '$_selectionKeyPrefix$accountKey';
  String _modeKey(String accountKey) => '$_modeKeyPrefix$accountKey';

  @override
  List<String>? readSelection(String accountKey) {
    final storage = _storage;
    if (storage == null) return null;
    final raw = storage.callMethod('getItem'.toJS, _selectionKey(accountKey).toJS);
    if (raw == null || !raw.isA<JSString>()) return null;
    final json = (raw as JSString).toDart;
    try {
      final decoded = jsonDecode(json);
      if (decoded is! List) return null;
      return decoded.map((e) => e as String).toList(growable: false);
    } catch (_) {
      return null;
    }
  }

  @override
  void writeSelection(String accountKey, List<String> bundleIds) {
    final storage = _storage;
    if (storage == null) return;
    storage.callMethod(
      'setItem'.toJS,
      _selectionKey(accountKey).toJS,
      jsonEncode(bundleIds).toJS,
    );
  }

  @override
  String? readMode(String accountKey) {
    final storage = _storage;
    if (storage == null) return null;
    final raw = storage.callMethod('getItem'.toJS, _modeKey(accountKey).toJS);
    if (raw == null || !raw.isA<JSString>()) return null;
    return (raw as JSString).toDart;
  }

  @override
  void writeMode(String accountKey, String mode) {
    final storage = _storage;
    if (storage == null) return;
    storage.callMethod(
      'setItem'.toJS,
      _modeKey(accountKey).toJS,
      mode.toJS,
    );
  }
}
