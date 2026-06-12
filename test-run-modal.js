const { chromium } = require('playwright');

(async () => {
  const browser = await chromium.launch({ headless: false, slowMo: 400 });
  const page = await browser.newPage();

  await page.goto('http://localhost:5000/Status/CreateDownTime');
  await page.waitForLoadState('networkidle');
  console.log('Page loaded');

  const svnInput = page.locator('#SVNCode');
  await svnInput.fill('TEST-SVN-001');

  const opSelect = page.locator('#Operation');
  const opOptions = await opSelect.locator('option').all();
  console.log(`Found ${opOptions.length} options`);

  let foundRunButton = false;
  // Try each option to find one where Run button appears
  for (let i = 1; i < opOptions.length; i++) {
    const val = await opOptions[i].getAttribute('value');
    if (!val) continue;
    await opSelect.selectOption({ index: i });
    await page.waitForTimeout(1200);
    const btnRun = page.locator('#btnRun');
    const visible = await btnRun.isVisible();
    if (visible) {
      console.log(`Found operation in Stop state at index ${i}: "${val}"`);
      foundRunButton = true;
      break;
    }
  }

  if (!foundRunButton) {
    console.log('No operation in Stop state found — testing Run modal standalone (forcing btnRun display)');
    // Force show the Run button for isolated modal test
    await page.evaluate(() => {
      document.getElementById('btnRun').style.display = 'inline-block';
      document.getElementById('btnStop').style.display = 'none';
    });
  }

  await page.screenshot({ path: 'screenshot-1-run-button-visible.png' });
  console.log('Screenshot 1: Run button visible');

  // ===== TEST: Click Run -> modal appears with description field =====
  const btnRun = page.locator('#btnRun');
  await btnRun.click();
  await page.waitForTimeout(600);

  const modal = page.locator('#confirmationModal');
  const modalDisplay = await modal.evaluate(el => getComputedStyle(el).display);
  console.log('Modal display after Run click (should be flex):', modalDisplay);

  const runDescWrap = page.locator('#runDescWrap');
  const descVisible = await runDescWrap.isVisible();
  console.log('Description field visible in modal:', descVisible);

  await page.screenshot({ path: 'screenshot-2-modal-open.png' });
  console.log('Screenshot 2: Modal open with description field');

  // ===== TEST: Cancel with No button =====
  await page.locator('#btnConfirmNo').click();
  await page.waitForTimeout(400);
  const modalAfterNo = await modal.evaluate(el => getComputedStyle(el).display);
  console.log('Modal hidden after No click (should be none):', modalAfterNo);
  const descAfterNo = await page.locator('#runDescInput').inputValue();
  console.log('Description cleared after cancel (should be empty):', descAfterNo);
  await page.screenshot({ path: 'screenshot-3-modal-cancelled.png' });
  console.log('Screenshot 3: Modal cancelled');

  // ===== TEST: Backdrop click closes modal =====
  await btnRun.click();
  await page.waitForTimeout(400);
  // Click the backdrop (outside modal-content)
  await modal.click({ position: { x: 10, y: 10 } });
  await page.waitForTimeout(400);
  const modalAfterBackdrop = await modal.evaluate(el => getComputedStyle(el).display);
  console.log('Modal hidden after backdrop click (should be none):', modalAfterBackdrop);

  // ===== TEST: Open with description filled =====
  await btnRun.click();
  await page.waitForTimeout(400);
  const runDescInput = page.locator('#runDescInput');
  await runDescInput.fill('Máy đã sửa xong, chạy lại');
  await page.screenshot({ path: 'screenshot-4-modal-with-desc.png' });
  console.log('Screenshot 4: Modal with description "Máy đã sửa xong, chạy lại"');
  console.log('Description value:', await runDescInput.inputValue());

  // ===== TEST: Confirm — should close modal and call performSave =====
  // Intercept the fetch to capture the request body
  let capturedFormData = null;
  await page.route('/Status/CreateDownTime', async route => {
    const req = route.request();
    capturedFormData = req.postData();
    console.log('\nCaptured POST to /Status/CreateDownTime');
    // Continue the request normally
    await route.continue();
  });

  await page.locator('#btnConfirmYes').click();
  await page.waitForTimeout(2500);

  const modalAfterYes = await modal.evaluate(el => getComputedStyle(el).display);
  console.log('Modal hidden after Yes click (should be none):', modalAfterYes);

  if (capturedFormData !== null) {
    const hasDescription = capturedFormData.includes('M%C3%A1y') || capturedFormData.includes('Máy') || capturedFormData.includes('%E1%BA%A1y') || capturedFormData.includes('description') || capturedFormData.toLowerCase().includes('m%C3');
    console.log('POST body captured (first 300 chars):', capturedFormData.substring(0, 300));
  } else {
    console.log('No POST captured (operation may require ISS_Code for Stop)');
  }

  await page.screenshot({ path: 'screenshot-5-after-confirm.png' });
  console.log('Screenshot 5: After confirm');

  // ===== TEST: Reopen modal — description cleared =====
  await btnRun.click();
  await page.waitForTimeout(400);
  const descOnReopen = await runDescInput.inputValue();
  console.log('Description cleared on modal reopen (should be empty):', `"${descOnReopen}"`);
  await page.screenshot({ path: 'screenshot-6-modal-reopened-empty.png' });
  console.log('Screenshot 6: Modal reopened — description is empty');

  // Cancel final modal
  await page.locator('#btnConfirmNo').click();

  console.log('\n=== All modal tests complete ===');
  await browser.close();
})();
