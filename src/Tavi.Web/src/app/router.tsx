import { createBrowserRouter, Navigate } from 'react-router-dom';
import { AppShell } from './AppShell';
import { lazy } from 'react';

const WorldScreen = lazy(() => import('@/modules/world/screens/WorldScreen'));
const ScenarioScreen = lazy(() => import('@/modules/scenario/screens/ScenarioScreen'));
const PerformanceScreen = lazy(() => import('@/modules/performance/screens/PerformanceScreen'));
const WritingScreen = lazy(() => import('@/modules/writing/screens/WritingScreen'));
const ExtensionsScreen = lazy(() => import('@/modules/extensions/screens/ExtensionsScreen'));
const ModelSettingsScreen = lazy(() => import('@/modules/settings/screens/ModelSettingsScreen'));

export const router = createBrowserRouter([
  {
    path: '/',
    element: <AppShell />,
    children: [
      { index: true, element: <Navigate to="/world" replace /> },
      { path: 'world', element: <WorldScreen /> },
      { path: 'scenario', element: <ScenarioScreen /> },
      { path: 'performance', element: <PerformanceScreen /> },
      { path: 'writing', element: <WritingScreen /> },
      { path: 'settings/extensions', element: <ExtensionsScreen /> },
      { path: 'settings/model', element: <ModelSettingsScreen /> },
      { path: '*', element: <div className="fallback">页面未找到</div> },
    ],
  },
]);
