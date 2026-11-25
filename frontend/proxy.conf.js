module.exports = {
  "/api": {
    target: process.env.API_PROXY || "http://localhost:5080",
    secure: false,
    changeOrigin: true,
  },
};
