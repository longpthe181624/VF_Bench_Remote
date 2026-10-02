// Kiểm tra luồng FE bằng API và DOM giả; chạy: node scripts/test-auth-ui.cjs
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const html = fs.readFileSync(path.join(__dirname, '../backend/BenchConsole.Api/wwwroot/index.html'), 'utf8');
const source = html.match(/<script>([\s\S]*?)<\/script>/)[1];

class Element {
  constructor(tag = 'div') {
    this.tag = tag; this.style = {}; this.children = []; this.events = {};
    this.value = ''; this.hidden = false; this.textContent = '';
    const classes = new Set();
    this.classList = {
      add: x => classes.add(x), remove: x => classes.delete(x),
      contains: x => classes.has(x), toggle: x => classes.has(x) ? classes.delete(x) : classes.add(x),
    };
  }
  set innerHTML(value) { this.children = []; }
  setAttribute(key, value) { this[key] = value; }
  appendChild(child) { this.children.push(child); return child; }
  replaceChildren(...children) { this.children = children; }
  addEventListener(event, handler) { this.events[event] = handler; }
  focus() {}
  reset() {}
  querySelectorAll() { return []; }
  async fire(event) { await this.events[event]?.({ preventDefault() {} }); }
}

function setup(session = null) {
  const elements = new Map();
  for (const match of html.matchAll(/<([a-z]+)\b([^>]*\bid="([^"]+)"[^>]*)>/g)) {
    const element = new Element(match[1]);
    element.hidden = /\bhidden\b/.test(match[2]);
    elements.set(match[3], element);
  }
  const storage = new Map(session ? [['benchconsole.phien', JSON.stringify(session)]] : []);
  const requests = []; const responses = []; const intervals = [];
  const context = vm.createContext({
    document: {
      getElementById: id => { assert(elements.has(id), `Thiếu phần tử ${id}`); return elements.get(id); },
      createElement: tag => new Element(tag), createTextNode: text => text,
      querySelectorAll: () => [],
    },
    localStorage: { getItem: key => storage.get(key), setItem: (key, value) => storage.set(key, value), removeItem: key => storage.delete(key) },
    setInterval: fn => intervals.push(fn), setTimeout: () => 0, clearTimeout() {},
    URLSearchParams, Date, console, navigator: {},
    fetch: async (url, options) => {
      requests.push({ url, options });
      assert(responses.length, `API ngoài dự kiến: ${url}`);
      const { status = 200, body } = responses.shift();
      return { ok: status < 400, status, json: async () => body, text: async () => JSON.stringify(body) };
    },
  });
  // Cho phép kiểm tra HTML trước khi JS chạy và điều khiển bước khởi động.
  assert(elements.get('man-chinh').hidden);
  assert(elements.get('man-ghi-danh').hidden);
  vm.runInContext(source.replace('\nkhoiDong();', '\n'), context);
  return { context, elements, storage, requests, responses, intervals, run: code => vm.runInContext(code, context) };
}

const session = { accessToken: 'access', nguoiDung: { hoTen: 'Người thử', vaiTro: [], quyen: [] } };
const qr = { anhQr: 'data:image/png;base64,AA==', biMatChiaNhom: 'ABCD EFGH' };
const flatten = element => [element, ...element.children.filter(x => x instanceof Element).flatMap(flatten)];
function screen(t, expected) {
  for (const id of ['man-dang-nhap', 'man-ghi-danh', 'man-chinh'])
    assert.equal(t.elements.get(id).hidden, id !== expected, `${id} / ${expected}`);
}

(async () => {
  const t = setup();
  await t.run('khoiDong()');
  screen(t, 'man-dang-nhap');
  t.responses.push({ body: { canGhiDanhTotp: true, tokenGhiDanh: 'setup' } }, { body: qr });
  await t.elements.get('dn-form').fire('submit');
  screen(t, 'man-ghi-danh');
  assert.equal(t.storage.size, 0, 'Chưa xác nhận thì không lưu phiên');
  assert.equal(t.requests[1].options.headers.Authorization, 'Bearer setup');
  const controls = flatten(t.elements.get('dn-ghidanh'));
  assert(controls.some(x => x.tag === 'img' && x.src === qr.anhQr));
  const confirm = controls.find(x => x.tag === 'button');
  const beforePoll = t.requests.length;
  await t.run('load()');
  assert.equal(t.requests.length, beforePoll, 'Không tải dữ liệu ứng dụng khi đang quét QR');
  t.responses.push({ status: 401, body: { error: 'Mã không đúng' } });
  await confirm.fire('click');
  screen(t, 'man-ghi-danh');
  assert.equal(t.storage.size, 0);
  assert.equal(t.elements.get('err').textContent, 'Mã không đúng');
  assert(t.elements.get('err').classList.contains('show'));
  assert.equal(confirm.disabled, false);
  t.run('load = async () => {}; napTinhTrangTotp = async () => {};');
  t.responses.push({ body: { phien: session, maKhoiPhuc: ['ABCD-EFGH'] } });
  await confirm.fire('click');
  screen(t, 'man-chinh');
  assert.equal(JSON.parse(t.storage.get('benchconsole.phien')).accessToken, 'access');
  assert.equal(t.elements.get('dn-ghidanh').children.length, 0);

  // Bật tự nguyện cũng dùng màn QR riêng.
  t.responses.push({ body: qr });
  await t.elements.get('totp-bat').fire('click');
  screen(t, 'man-ghi-danh');
  assert.equal(t.storage.size, 0);
  await t.elements.get('dn-quay-lai').fire('click');
  screen(t, 'man-dang-nhap');

  const failed = setup();
  failed.responses.push({ status: 401, body: { error: 'Token tạm hết hạn' } });
  await failed.run('moManGhiDanh("setup")');
  screen(failed, 'man-ghi-danh');
  assert.equal(failed.elements.get('err').textContent, 'Token tạm hết hạn');
  await failed.elements.get('dn-quay-lai').fire('click');
  screen(failed, 'man-dang-nhap');

  const second = setup();
  await second.run('khoiDong()');
  second.responses.push({ body: { canMaTotp: true } });
  await second.elements.get('dn-form').fire('submit');
  screen(second, 'man-dang-nhap');
  assert.equal(second.elements.get('dn-buoc2').style.display, '');
  assert.equal(second.storage.size, 0);

  const saved = setup(session);
  saved.run('load = async () => {}; napTinhTrangTotp = async () => {};');
  saved.responses.push({ body: session.nguoiDung });
  const starting = saved.run('khoiDong()');
  screen(saved, 'man-dang-nhap');
  await starting;
  screen(saved, 'man-chinh');
  assert.equal(saved.requests[0].url, '/api/auth/me');

  const expired = setup(session);
  expired.responses.push({ status: 401, body: {} });
  await expired.run('khoiDong()');
  screen(expired, 'man-dang-nhap');
  assert.equal(expired.storage.size, 0);
  console.log('OK: màn riêng cho QR, mã sai/đúng, quay lại, lỗi tải QR, TOTP, phiên lưu hợp lệ/hết hạn.');
})().catch(error => { console.error(error); process.exitCode = 1; });
