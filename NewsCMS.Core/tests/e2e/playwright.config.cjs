// @ts-check
const { defineConfig, devices } = require('@playwright/test');

/**
 * Test hồi quy trình duyệt cho Site Builder. Cố ý KHÔNG có webServer: các test ở đây dựng DOM +
 * iframe bằng chính chúng (data: URL), không cần app chạy — nên chạy được trong CI không có DB.
 * Bug lệch realm (0.1) là bug thuần trình duyệt (cross-realm instanceof), tái tạo được offline.
 */
module.exports = defineConfig({
  testDir: '.',
  fullyParallel: true,
  reporter: [['list']],
  use: { ...devices['Desktop Chrome'] },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
