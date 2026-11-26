import 'dart:async';
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
    this.prefetchAhead = 2,
  });

  final SloomAtlas atlas;
  final String animationUri;
  final double scale;
  final bool autoPlay;

  /// Number of upcoming unique pages decoded in the background. Set to zero
  /// to load pages only when they are about to be drawn.
  final int prefetchAhead;

  @override
  State<SloomPlayer> createState() => _SloomPlayerState();
}

class _SloomPlayerState extends State<SloomPlayer>
    with SingleTickerProviderStateMixin {
  AnimationController? _controller;
  SloomAnimation? _animation;
  List<SloomFrame> _frames = const [];
  List<int> _pageOrder = const [];
  final Set<int> _pendingPages = {};
  final Set<int> _failedPages = {};
  String? _error;
  int _generation = 0;
  bool _prefetching = false;
  int? _prefetchPage;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void didUpdateWidget(SloomPlayer oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.animationUri == widget.animationUri &&
        oldWidget.atlas == widget.atlas) {
      return;
    }

    _generation++;
    _controller?.dispose();
    _controller = null;
    _animation = null;
    _frames = const [];
    _pageOrder = const [];
    _pendingPages.clear();
    _failedPages.clear();
    _prefetchPage = null;
    _error = null;
    unawaited(_load());
  }

  Future<void> _load() async {
    final generation = ++_generation;

    try {
      final animation =
          await widget.atlas.package.animation(widget.animationUri);
      if (!mounted || generation != _generation) {
        return;
      }

      final frames = [
        for (final uri in animation.frameUris) widget.atlas.package.frame(uri)
      ];
      if (frames.isEmpty) {
        throw StateError('Animation has no frames: ${widget.animationUri}');
      }

      final pageOrder = <int>[];
      final seenPages = <int>{};
      for (final frame in frames) {
        if (seenPages.add(frame.page)) {
          pageOrder.add(frame.page);
        }
      }

      final duration = animation.totalDurationMs;
      final controller = AnimationController(
        vsync: this,
        duration: Duration(milliseconds: duration == 0 ? 1 : duration),
      )..addListener(_onTick);

      setState(() {
        _animation = animation;
        _frames = frames;
        _pageOrder = pageOrder;
        _controller = controller;
        _error = null;
      });

      // Only the first page is required before playback starts. The remaining
      // pages are decoded ahead of the current frame while it runs.
      await widget.atlas.image(frames.first.page);
      if (!mounted || generation != _generation) {
        return;
      }

      setState(() {});

      if (widget.autoPlay) {
        if (animation.loop) {
          controller.repeat();
        } else {
          controller.forward();
        }
      }

      _prefetchWindow();
    } catch (error) {
      if (mounted && generation == _generation) {
        setState(() => _error = error.toString());
      }
    }
  }

  void _onTick() {
    if (!mounted) {
      return;
    }

    setState(() {});
    _prefetchWindow();
  }

  void _prefetchWindow() {
    if (widget.prefetchAhead <= 0 || _pageOrder.length <= 1) {
      return;
    }

    final current = _currentPage();
    if (current == null || current == _prefetchPage || _prefetching) {
      return;
    }

    _prefetchPage = current;
    _prefetching = true;
    unawaited(_prefetchWindowAsync(_generation, current));
  }

  Future<void> _prefetchWindowAsync(int generation, int originPage) async {
    try {
      final origin = _pageOrder.indexOf(originPage);
      if (origin < 0) {
        return;
      }

      for (var step = 1; step <= widget.prefetchAhead; step++) {
        if (!mounted || generation != _generation) {
          return;
        }

        final page = _pageOrder[(origin + step) % _pageOrder.length];
        if (widget.atlas.isLoaded(page) || _failedPages.contains(page)) {
          continue;
        }

        try {
          await widget.atlas.image(page);
          await Future<void>.delayed(Duration.zero);
        } catch (_) {
          _failedPages.add(page);
        }
      }
    } finally {
      _prefetching = false;
    }
  }

  void _ensurePage(int page) {
    if (widget.atlas.isLoaded(page) || _pendingPages.contains(page)) {
      return;
    }

    _pendingPages.add(page);
    widget.atlas.image(page).then(
      (_) {
        if (mounted) {
          setState(() {});
        }
      },
      onError: (Object error, StackTrace stack) {
        _failedPages.add(page);
        if (mounted) {
          setState(() => _error = error.toString());
        }
      },
    ).whenComplete(() => _pendingPages.remove(page));
  }

  @override
  void dispose() {
    _generation++;
    _controller?.dispose();
    _controller = null;
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
    final controller = _controller;
    final animation = _animation;
    if (frames.isEmpty || controller == null || animation == null) {
      return const SizedBox.square(dimension: 32);
    }

    final frame = frames[_currentIndex(frames, animation)];
    if (!widget.atlas.isLoaded(frame.page)) {
      _ensurePage(frame.page);
    }

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

  int? _currentPage() {
    final frames = _frames;
    final animation = _animation;
    final controller = _controller;
    if (frames.isEmpty || animation == null || controller == null) {
      return null;
    }

    return frames[_currentIndex(frames, animation)].page;
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
