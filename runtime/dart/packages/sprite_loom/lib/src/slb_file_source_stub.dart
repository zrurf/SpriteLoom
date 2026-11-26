import 'slb_byte_source.dart';

Future<SloomSlbByteSource> openSlbFileSource(
  String path, {
  bool preferMmap = true,
}) => Future.error(UnsupportedError('SLB file sources require dart:io.'));
