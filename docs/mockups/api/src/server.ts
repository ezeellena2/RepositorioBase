import express from "express";

const PORT = 3001;
const app = express();

app.use(express.json());

app.get("/api/health", (_req, res) => {
  res.json({ status: "ok", time: new Date().toISOString() });
});

app.listen(PORT, () => {
  console.log(`API en http://localhost:${PORT}`);
});
