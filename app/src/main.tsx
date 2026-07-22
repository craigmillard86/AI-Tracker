import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';

// Self-hosted fonts (constitution: no runtime network — no Google Fonts).
import '@fontsource/montserrat/700.css';
import '@fontsource/inter-tight/400.css';
import '@fontsource/inter-tight/500.css';
import '@fontsource/inter-tight/700.css';

import './design/tokens.css';
import './shell/AppShell.css';
import './screens/signin/SignInScreen.css';
import './components/LevelSelectorCard/LevelSelectorCard.css';
import './components/ProgressStepper/ProgressStepper.css';
import './components/PurposeBanner/PurposeBanner.css';
import './screens/assessment-self/AssessmentSelfScreen.css';
import { App } from './App';

const rootElement = document.getElementById('root');
if (!rootElement) {
  throw new Error('Root element #root not found');
}

createRoot(rootElement).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
