import 'package:sprite_loom/sprite_loom.dart';
import 'package:test/test.dart';

void main() {
  test('libsodium provides native AEAD', () {
    expect(SloomCryptoCapabilities.nativeXChaCha20Poly1305, isTrue);
    expect(SloomCryptoCapabilities.nativeAesGcm, isTrue);
  });
}
