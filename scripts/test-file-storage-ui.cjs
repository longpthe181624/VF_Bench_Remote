// Trước khi chạy: dotnet run --project backend/BenchConsole.Api.Tests -- --serve-ui
// PLAYWRIGHT_MODULE / CHROMIUM_PATH có thể chỉ tới bản Playwright và browser cài sẵn.
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const assert=require('node:assert/strict');
const fs=require('node:fs/promises');
function zip(name,text){
  const bytes=Buffer.from(text),filename=Buffer.from(name);let crc=0xffffffff;
  for(const byte of bytes){crc^=byte;for(let i=0;i<8;i++)crc=crc&1?(crc>>>1)^0xedb88320:crc>>>1;}
  crc=(crc^0xffffffff)>>>0;
  const local=Buffer.alloc(30);local.writeUInt32LE(0x04034b50);local.writeUInt32LE(crc,14);local.writeUInt32LE(bytes.length,18);local.writeUInt32LE(bytes.length,22);local.writeUInt16LE(filename.length,26);
  const central=Buffer.alloc(46);central.writeUInt32LE(0x02014b50);central.writeUInt32LE(crc,16);central.writeUInt32LE(bytes.length,20);central.writeUInt32LE(bytes.length,24);central.writeUInt16LE(filename.length,28);
  const end=Buffer.alloc(22);end.writeUInt32LE(0x06054b50);end.writeUInt16LE(1,8);end.writeUInt16LE(1,10);end.writeUInt32LE(46+filename.length,12);end.writeUInt32LE(30+filename.length+bytes.length,16);
  return Buffer.concat([local,filename,bytes,central,filename,end]);
}
(async()=>{
  const browser=await chromium.launch({headless:true,...(process.env.CHROMIUM_PATH?{executablePath:process.env.CHROMIUM_PATH}:{})});
  try{
    const page=await browser.newPage({viewport:{width:1440,height:1000}}),errors=[];
    page.on('pageerror',e=>errors.push(e.message));
    await page.goto(process.env.STORAGE_UI_URL||'http://127.0.0.1:5077/index.html');
    await page.fill('#dn-email','admin@benchconsole.local');await page.fill('#dn-mk','Admin@12345');await page.locator('#dn-form button[type=submit]').click();await page.locator('#man-chinh').waitFor({state:'visible'});
    const text='File thật được tải lên qua UI\n<script>alert("không được chạy")</script>';
    const payload=Buffer.from(text);
    await page.setInputFiles('#kho-file',{name:'ui-storage.txt',mimeType:'text/plain',buffer:payload});await page.fill('#kho-mota','Ghi chú upload');
    await page.locator('#kho-form button[type=submit]').click();
    let row=page.locator('#kho-area tbody tr').filter({hasText:'ui-storage.txt'});await row.waitFor();
    const result=page.waitForEvent('download');await row.getByRole('button',{name:'Tải về',exact:true}).click();const downloaded=await result;
    assert.deepEqual(await fs.readFile(await downloaded.path()),payload);
    await row.getByRole('button',{name:'Thông tin / sửa',exact:true}).click();const dialog=page.locator('#file-dialog');
    await dialog.getByRole('button',{name:'Xem trước',exact:true}).click();assert.equal(await dialog.locator('pre').innerText(),text);assert.equal(await dialog.locator('script').count(),0);
    await dialog.locator('input').fill('ui-renamed.txt');await dialog.locator('textarea').fill('Ghi chú đã sửa');await dialog.getByRole('button',{name:'Lưu thay đổi'}).click();
    row=page.locator('#kho-area tbody tr').filter({hasText:'ui-renamed.txt'});await row.waitFor();assert.match(await row.innerText(),/Ghi chú đã sửa/);
    await page.fill('#kho-q','không có file này');assert.equal(await page.locator('#kho-area tbody tr').count(),0);await page.fill('#kho-q','ui-renamed');assert.equal(await page.locator('#kho-area tbody tr').count(),1);await page.fill('#kho-q','');
    // Upload hơn giới hạn Kestrel mặc định 30 MB; kiểm tổng và FormOptions thực tế.
    const large=Buffer.alloc(32*1024*1024,0x61);
    await page.setInputFiles('#kho-file',{name:'large-32mb.bin',mimeType:'application/octet-stream',buffer:large});await page.locator('#kho-form button[type=submit]').click();
    await page.locator('#kho-area tbody tr').filter({hasText:'large-32mb.bin'}).waitFor();
    // Token hết hạn phải được refresh và thử lại multipart, không mất file chọn.
    await page.evaluate(()=>{phien.accessToken='expired-test-token';});
    await page.setInputFiles('#kho-file',{name:'after-refresh.txt',mimeType:'text/plain',buffer:payload});await page.locator('#kho-form button[type=submit]').click();
    await page.locator('#kho-area tbody tr').filter({hasText:'after-refresh.txt'}).waitFor();
    await page.click('#dlc-toggle');await page.fill('#dlc-form input[name=ten]','UI shared');await page.setInputFiles('#dlc-form input[name=file]',{name:'shared.txt',mimeType:'text/plain',buffer:payload});await page.locator('#dlc-form button[type=submit]').click();
    const shared=page.locator('#dlc-area tbody tr').filter({hasText:'UI shared'});await shared.waitFor();await shared.getByRole('button',{name:'Thông tin / sửa'}).click();
    await dialog.locator('input').fill('UI shared moved');await dialog.locator('select').selectOption('khac');await dialog.getByRole('button',{name:'Lưu thay đổi'}).click();await page.locator('#file-dialog').waitFor({state:'hidden'});
    await page.locator('#dlc-muc').getByRole('button',{name:/Khác/}).click();await page.locator('#dlc-area tbody tr').filter({hasText:'UI shared moved'}).waitFor();
    await page.click('#toggle-goi');await page.selectOption('#goi-kieu','manual');await page.fill('#goi-ten','Manual_UI');await page.setInputFiles('#goi-file',{name:'manual.zip',mimeType:'application/zip',buffer:zip('case.xlsx','Excel fixture')});await page.locator('#goi-form button[type=submit]').click();
    const manual=page.locator('#goi-area tbody tr').filter({hasText:'Manual_UI'});await manual.waitFor();assert.match(await manual.innerText(),/Manual.*Excel/);assert.equal(await manual.getByRole('button',{name:'Đẩy xuống'}).count(),0);assert.equal(await manual.getByRole('button',{name:'Chạy',exact:true}).count(),0);
    const getZip=page.waitForEvent('download');await manual.getByRole('button',{name:'Tải về',exact:true}).click();assert.deepEqual(await fs.readFile(await(await getZip).path()),zip('case.xlsx','Excel fixture'));
    await page.setInputFiles('#kho-file',{name:'empty.txt',mimeType:'text/plain',buffer:Buffer.alloc(0)});await page.locator('#kho-form button[type=submit]').click();assert.match(await page.locator('#err').innerText(),/rỗng/);assert.equal(await page.locator('#kho-file').evaluate(n=>n.files.length),1);
    if(process.env.STORAGE_SCREENSHOT_DIR){
      await page.locator('#kho-area').screenshot({path:process.env.STORAGE_SCREENSHOT_DIR+'/storage-private.png'});
      await page.locator('#goi-area').screenshot({path:process.env.STORAGE_SCREENSHOT_DIR+'/storage-packages.png'});
    }
    page.once('dialog',d=>d.accept());await row.getByRole('button',{name:'Xoá',exact:true}).click();await row.waitFor({state:'detached'});
    assert.deepEqual(errors,[]);console.log('OK: UI + API thật upload/download đúng byte, file 32 MB, refresh khi upload, preview văn bản an toàn, sửa/chuyển mục, tìm kiếm, manual ZIP và lỗi file rỗng.');
  }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
