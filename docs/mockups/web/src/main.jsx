import React from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import './styles/tokens.css';
import './styles/base.css';
import Login from './pages/Login.jsx';
import Registro from './pages/Registro.jsx';
import RegistroEnviado from './pages/RegistroEnviado.jsx';
import Confirmar from './pages/Confirmar.jsx';
import App from './pages/App.jsx';

createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<Navigate to="/login" replace />} />
        <Route path="/login" element={<Login />} />
        <Route path="/registro" element={<Registro />} />
        <Route path="/registro/enviado" element={<RegistroEnviado />} />
        <Route path="/confirmar" element={<Confirmar />} />
        <Route path="/app" element={<App />} />
        <Route path="*" element={<Navigate to="/login" replace />} />
      </Routes>
    </BrowserRouter>
  </React.StrictMode>
);
