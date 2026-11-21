import 'dart:ui' as ui;

import 'package:flutter/widgets.dart';
import 'package:sprite_loom/sprite_loom.dart';

import 'atlas.dart';

class SloomPlayer extends StatefulWidget {
  const SloomPlayer({
    super.key,
    required this.atlas,
    required this.animationUri,
    this.scale = 1.0,
    this.autoPlay = true,
  });

  final SloomAtlas atlas;
  final String animationUri;
  final double scale;
  final bool autoPlay;

  @override
  State<SloomPlayer> createState() => _SloomPlayerState();
}

class _SloomPlayerState extends State<SloomPlayer>
    with SingleTickerProviderStateMixin {
  AnimationController? _controller;
  SloomAnimation? _animation;
  List<SloomFrame> _frames = const [];
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final animation = await widget.atlas.package.animation(widget.animationUri);
      final frames = [
        for (final uri in animation.frameUris) widget.atlas.package.frame(uri)
      ];
      final pages = frames.map((frame) => frame.page);
      await widget.atlas.preload(pages);

      if (!mounted) {
        return;
      }

      final duration = animation.totalDurationMs;
      final controller = AnimationController(
        vsync: this,
        duration: Duration(milliseconds: duration == 0 ? 1 : duration),
      )..addListener(() => setState(() {}));

      setState(() {
        _animation = animation;
        _frames = frames;
        _controller = controller;
      });

      if (widget.autoPlay) {
        if (animation.loop) {
          controller.repeat();
        } else {
          controller.forward();
        }
      }
    } catch (error) {
      if (mounted) {
        setState(() => _error = error.toString());
      }
    }
  }

  @override
  void dispose() {
    _controller?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (_error != null) {
      return Text(
        _error!,
        style: const TextStyle(color: Color(0xFFB00020)),
      );
    }

    final frames = _frames;
    if (frames.isEmpty || _controller == null) {
      return const SizedBox.square(dimension: 32);
    }

    final frame = frames[_currentIndex(frames, _animation!)];
    final size = Size(
      frame.originWidth * widget.scale,
      frame.originHeight * widget.scale,
    );
    final image = widget.atlas.imageIfLoaded(frame.page);

    return SizedBox.fromSize(
      size: size,
      child: CustomPaint(
        painter: _SloomFramePainter(
          frame: frame,
          image: image,
          scale: widget.scale,
        ),
      ),
    );
  }

  int _currentIndex(List<SloomFrame> frames, SloomAnimation animation) {
    final total = animation.totalDurationMs;
    if (total <= 0 || frames.length == 1) {
      return 0;
    }

    var progress = _controller!.value;
    switch (animation.playback) {
      case 'reverse':
        progress = 1.0 - progress;
      case 'pingpong':
        progress = progress < 0.5 ? progress * 2 : (1.0 - progress) * 2;
    }

    var elapsed = (progress * total).floor();
    if (animation.loop) {
      elapsed %= total;
    } else {
      elapsed = elapsed.clamp(0, total - 1);
    }

    var cursor = 0;
    for (var index = 0; index < frames.length; index++) {
      cursor += index < animation.durationsMs.length
          ? animation.durationsMs[index]
          : 0;
      if (elapsed < cursor) {
        return index;
      }
    }

    return frames.length - 1;
  }
}

class _SloomFramePainter extends CustomPainter {
  _SloomFramePainter({
    required this.frame,
    required this.image,
    required this.scale,
  });

  final SloomFrame frame;
  final ui.Image? image;
  final double scale;

  @override
  void paint(Canvas canvas, Size size) {
    final source = image;
    if (source == null || frame.rotated) {
      return;
    }

    final src = Rect.fromLTWH(
      frame.rect.x,
      frame.rect.y,
      frame.rect.width,
      frame.rect.height,
    );
    final dst = Rect.fromLTWH(
      frame.offsetX * scale,
      frame.offsetY * scale,
      frame.rect.width * scale,
      frame.rect.height * scale,
    );
    final paint = Paint()..filterQuality = FilterQuality.medium;
    canvas.drawImageRect(source, src, dst, paint);
  }

  @override
  bool shouldRepaint(_SloomFramePainter oldDelegate) =>
      oldDelegate.frame != frame ||
      oldDelegate.image != image ||
      oldDelegate.scale != scale;
}
