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
import './components/DivergenceFlag/DivergenceFlag.css';
import './components/ComparisonRow/ComparisonRow.css';
import './components/StatTile/StatTile.css';
import './components/DimensionBar/DimensionBar.css';
import './components/TrendSparkline/TrendSparkline.css';
import './components/SuppressedCell/SuppressedCell.css';
import './components/StaleRowFlag/StaleRowFlag.css';
import './components/LevelBadge/LevelBadge.css';
import './components/RagChip/RagChip.css';
import './components/StageTimeline/StageTimeline.css';
import './components/NRLineEditor/NRLineEditor.css';
import './components/EvidencePanel/EvidencePanel.css';
import './components/DeclarationLevelPicker/DeclarationLevelPicker.css';
import './screens/assessment-self/AssessmentSelfScreen.css';
import './screens/assessment-moderation/AssessmentModerationScreen.css';
import './screens/dashboard-bu/DashboardScreen.css';
import './screens/register-list/RegisterListScreen.css';
import './screens/register-detail/RegisterDetailScreen.css';
import './screens/bu-forms/BuFormsScreen.css';
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
