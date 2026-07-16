import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { ArchyApp } from './app';
import './styles.css';

createRoot(document.getElementById('root')!).render(<StrictMode><ArchyApp /></StrictMode>);
