import '@fontsource/manrope/400.css';
import '@fontsource/manrope/500.css';
import '@fontsource/manrope/600.css';
import '@fontsource/manrope/700.css';
import '@fontsource/source-sans-3/400.css';
import '@fontsource/source-sans-3/500.css';
import '@fontsource/source-sans-3/600.css';
import React from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import App from './App';
import './i18n';

const failureType = (failure) => (
  typeof failure?.name === 'string' && failure.name.length > 0 ? failure.name : 'Error'
);
const reportUiFailure = (failure, info) => {
  console.error('ui_failure', failureType(failure), info?.componentStack);
};
const reportUnhandledRejection = (event) => {
  console.error('unhandled_rejection', failureType(event.reason));
};

window.addEventListener('unhandledrejection', reportUnhandledRejection);

const baseUrl = document.getElementsByTagName('base')[0].getAttribute('href');
const root = createRoot(document.getElementById('root'), {
  onCaughtError: reportUiFailure,
  onUncaughtError: reportUiFailure,
});

root.render(
  <BrowserRouter basename={baseUrl}>
    <App />
  </BrowserRouter>
);
