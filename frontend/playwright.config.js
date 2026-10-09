import { defineConfig } from "@playwright/test";

const port = 8081;

export default defineConfig({
  testDir: "tests",
  use: {
    baseURL: `http://localhost:${port}`,
  },
  webServer: {
    command: `node serve.mjs ${port}`,
    url: `http://localhost:${port}`,
    reuseExistingServer: !process.env.CI,
  },
});
