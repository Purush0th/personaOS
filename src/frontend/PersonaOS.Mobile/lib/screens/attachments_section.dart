import 'dart:io';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:open_filex/open_filex.dart';
import 'package:path_provider/path_provider.dart';

import '../api/personaos_api.dart';
import '../date_utils.dart';

/// The files attached to a task or goal: open, add and delete them, as on the web. A file opens
/// in whatever app the phone has for it, after it is downloaded to the app's own folder.
class AttachmentsSection extends StatefulWidget {
  const AttachmentsSection({super.key, required this.api, required this.itemType, required this.itemKey});

  final PersonaOsApi api;

  /// "task" or "goal".
  final String itemType;
  final String itemKey;

  @override
  State<AttachmentsSection> createState() => _AttachmentsSectionState();
}

class _AttachmentsSectionState extends State<AttachmentsSection> {
  late Future<List<WorkItemAttachment>> _files = widget.api.getAttachments(widget.itemType, widget.itemKey);
  bool _busy = false;

  Future<void> _reload() async {
    final reloaded = widget.api.getAttachments(widget.itemType, widget.itemKey);
    setState(() {
      _files = reloaded;
    });
    await reloaded;
  }

  void _show(String message) {
    if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));
  }

  Future<void> _run(Future<void> Function() action) async {
    setState(() => _busy = true);
    try {
      await action();
    } on ApiException catch (e) {
      _show(e.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _add() async {
    final picked = await FilePicker.platform.pickFiles();
    final file = picked?.files.single;
    if (file == null || file.path == null) return;
    await _run(() async {
      await widget.api.uploadAttachment(widget.itemType, widget.itemKey, filePath: file.path!, fileName: file.name);
      await _reload();
    });
  }

  Future<void> _open(WorkItemAttachment attachment) => _run(() async {
        final bytes = await widget.api.downloadAttachment(attachment.id);
        final dir = await getApplicationDocumentsDirectory();
        final folder = Directory('${dir.path}/attachments');
        await folder.create(recursive: true);
        final file = File('${folder.path}/${attachment.id}-${attachment.fileName}');
        await file.writeAsBytes(bytes, flush: true);
        final result = await OpenFilex.open(file.path, type: attachment.contentType);
        if (result.type != ResultType.done) _show('Saved, but no app here opens ${attachment.fileName}.');
      });

  Future<void> _delete(WorkItemAttachment attachment) async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Delete attachment?'),
        content: Text('${attachment.fileName} will be deleted for good.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Delete')),
        ],
      ),
    );
    if (ok != true) return;
    await _run(() async {
      await widget.api.deleteAttachment(attachment.id);
      await _reload();
    });
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return FutureBuilder<List<WorkItemAttachment>>(
      future: _files,
      builder: (context, snapshot) {
        final files = snapshot.data ?? const <WorkItemAttachment>[];
        return Column(
          key: const Key('attachments'),
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(files.isEmpty ? 'Attachments' : 'Attachments · ${files.length}',
                      style: theme.textTheme.titleSmall),
                ),
                TextButton.icon(
                  onPressed: _busy ? null : _add,
                  icon: const Icon(Icons.attach_file, size: 18),
                  label: const Text('Attach'),
                ),
              ],
            ),
            if (_busy) const LinearProgressIndicator(),
            if (snapshot.connectionState == ConnectionState.done && snapshot.hasError)
              Text('Could not load the attachments.', style: theme.textTheme.bodySmall)
            else if (snapshot.connectionState == ConnectionState.done && files.isEmpty)
              Text('No attachments yet.', style: theme.textTheme.bodySmall),
            for (final file in files)
              ListTile(
                key: Key('attachment-${file.id}'),
                dense: true,
                contentPadding: EdgeInsets.zero,
                leading: const Icon(Icons.insert_drive_file_outlined),
                title: Text(file.fileName, maxLines: 1, overflow: TextOverflow.ellipsis),
                subtitle: Text('${_size(file.sizeBytes)} · ${relativeTime(file.createdAtUtc)}'),
                onTap: _busy ? null : () => _open(file),
                trailing: IconButton(
                  tooltip: 'Delete attachment',
                  icon: const Icon(Icons.delete_outline),
                  onPressed: _busy ? null : () => _delete(file),
                ),
              ),
          ],
        );
      },
    );
  }
}

String _size(int bytes) {
  if (bytes < 1024) return '$bytes B';
  if (bytes < 1024 * 1024) return '${(bytes / 1024).toStringAsFixed(0)} KB';
  return '${(bytes / (1024 * 1024)).toStringAsFixed(1)} MB';
}
