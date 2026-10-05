// getRandomValues dùng được trên HTTP nội bộ, không phụ thuộc randomUUID/secure context.
export function createRequestCode() {
  const bytes = globalThis.crypto.getRandomValues(new Uint8Array(8))
  return 'REQ-' + Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('').toUpperCase()
}
