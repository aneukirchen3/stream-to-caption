/**
 * PodtextCaption - Frontend Controller
 * Supports dual-page routing (/import and /listen/{id})
 * Uses O(log N) Binary Search for high-performance synchronized transcript playback.
 */

document.addEventListener('DOMContentLoaded', () => {
    // --- Route & Navigation Controller ---
    const path = window.location.pathname;
    const isListenPage = path.startsWith('/listen/');
    const isImportPage = path.startsWith('/import') || path === '/';

    // Highlight active nav link
    const navImport = document.getElementById('navImport');
    const navLibrary = document.getElementById('navLibrary');
    if (navImport && navLibrary) {
        if (window.location.hash === '#library') {
            navLibrary.classList.add('active');
            navImport.classList.remove('active');
        } else if (isImportPage) {
            navImport.classList.add('active');
            navLibrary.classList.remove('active');
        }
    }

    // Theme Controller
    const themeToggleBtn = document.getElementById('themeToggleBtn');
    const savedTheme = localStorage.getItem('podtext_theme') || 'dark';
    document.documentElement.setAttribute('data-theme', savedTheme);

    if (themeToggleBtn) {
        themeToggleBtn.addEventListener('click', () => {
            const current = document.documentElement.getAttribute('data-theme');
            const next = current === 'dark' ? 'light' : 'dark';
            document.documentElement.setAttribute('data-theme', next);
            localStorage.setItem('podtext_theme', next);
        });
    }

    // Health check
    checkSystemHealth();

    // --- IMPORT PAGE LOGIC ---
    if (isImportPage) {
        initImportPage();
    }

    // --- LISTEN PAGE LOGIC ---
    if (isListenPage) {
        const podcastId = path.split('/listen/')[1];
        if (podcastId) {
            initListenPage(podcastId);
        }
    }

    // ==========================================
    // IMPORT PAGE FUNCTIONS
    // ==========================================
    function initImportPage() {
        const transForm = document.getElementById('transcribeForm');
        const urlInput = document.getElementById('urlInput');
        const fileInput = document.getElementById('fileInput');
        const langSelect = document.getElementById('langSelect');
        const modelSelect = document.getElementById('modelSelect');
        const submitBtn = document.getElementById('submitBtn');
        const tabUrl = document.getElementById('tabUrl');
        const tabUpload = document.getElementById('tabUpload');
        const urlInputGroup = document.getElementById('urlInputGroup');
        const uploadInputGroup = document.getElementById('uploadInputGroup');

        if (tabUrl && tabUpload) {
            tabUrl.addEventListener('click', () => {
                tabUrl.classList.add('active');
                tabUpload.classList.remove('active');
                urlInputGroup.style.display = 'flex';
                uploadInputGroup.style.display = 'none';
            });

            tabUpload.addEventListener('click', () => {
                tabUpload.classList.add('active');
                tabUrl.classList.remove('active');
                uploadInputGroup.style.display = 'block';
                urlInputGroup.style.display = 'none';
            });
        }

        if (transForm) {
            transForm.addEventListener('submit', async (e) => {
                e.preventDefault();
                const isUrlTab = tabUrl.classList.contains('active');
                const lang = langSelect.value;
                const model = modelSelect.value;

                if (submitBtn) submitBtn.disabled = true;

                if (isUrlTab) {
                    const url = urlInput.value.trim();
                    if (!url) {
                        alert('Please enter a valid podcast/audio URL.');
                        if (submitBtn) submitBtn.disabled = false;
                        return;
                    }
                    await startUrlTranscription(url, lang, model);
                } else {
                    const file = fileInput.files[0];
                    if (!file) {
                        alert('Please select an audio file to upload.');
                        if (submitBtn) submitBtn.disabled = false;
                        return;
                    }
                    await startFileTranscription(file, lang, model);
                }
            });
        }

        loadPodcastLibrary();
    }

    async function startUrlTranscription(url, language, model) {
        showProgressCard('Initiating audio URL import...');
        try {
            const res = await fetch('/api/podcast/transcribe', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ url, language, model })
            });

            if (!res.ok) {
                const err = await res.json();
                throw new Error(err.error || 'Failed to start transcription.');
            }

            const podcast = await res.json();
            pollJobStatus(podcast.id);
        } catch (err) {
            alert(`Error: ${err.message}`);
            hideProgressCard();
            const submitBtn = document.getElementById('submitBtn');
            if (submitBtn) submitBtn.disabled = false;
        }
    }

    async function startFileTranscription(file, language, model) {
        showProgressCard('Uploading local audio file...');
        try {
            const formData = new FormData();
            formData.append('file', file);
            formData.append('language', language);
            formData.append('model', model);

            const res = await fetch('/api/podcast/upload', {
                method: 'POST',
                body: formData
            });

            if (!res.ok) {
                const err = await res.json();
                throw new Error(err.error || 'Failed to upload audio file.');
            }

            const podcast = await res.json();
            pollJobStatus(podcast.id);
        } catch (err) {
            alert(`Error: ${err.message}`);
            hideProgressCard();
            const submitBtn = document.getElementById('submitBtn');
            if (submitBtn) submitBtn.disabled = false;
        }
    }

    function pollJobStatus(podcastId) {
        const interval = setInterval(async () => {
            try {
                const res = await fetch(`/api/podcast/${podcastId}`);
                if (!res.ok) return;
                const podcast = await res.json();

                if (podcast.status === 'Completed') {
                    clearInterval(interval);
                    hideProgressCard();
                    const submitBtn = document.getElementById('submitBtn');
                    if (submitBtn) submitBtn.disabled = false;
                    
                    showCompletedCard(podcast);
                    loadPodcastLibrary();
                } else if (podcast.status === 'Failed') {
                    clearInterval(interval);
                    hideProgressCard();
                    const submitBtn = document.getElementById('submitBtn');
                    if (submitBtn) submitBtn.disabled = false;
                    alert(`Transcription failed: ${podcast.errorMessage || 'Unknown error'}`);
                    loadPodcastLibrary();
                } else {
                    updateProgressUI(podcast.status);
                }
            } catch (e) {
                console.error('Polling error:', e);
            }
        }, 1500);
    }

    function updateProgressUI(status) {
        let pct = 10;
        let msg = 'Processing...';

        const stepDownload = document.getElementById('stepDownload');
        const stepConvert = document.getElementById('stepConvert');
        const stepTranscribe = document.getElementById('stepTranscribe');
        const stepGenerate = document.getElementById('stepGenerate');

        resetStepIcons();

        switch (status) {
            case 'Downloading':
                pct = 25;
                msg = 'Downloading audio from URL...';
                setStepState(stepDownload, '●');
                break;
            case 'Converting':
                pct = 50;
                msg = 'Normalizing audio format (16kHz mono)...';
                setStepState(stepDownload, '✓');
                setStepState(stepConvert, '●');
                break;
            case 'Transcribing':
                pct = 75;
                msg = 'Transcribing speech locally using Whisper...';
                setStepState(stepDownload, '✓');
                setStepState(stepConvert, '✓');
                setStepState(stepTranscribe, '●');
                break;
            case 'Processing':
                pct = 90;
                msg = 'Generating word timestamps...';
                setStepState(stepDownload, '✓');
                setStepState(stepConvert, '✓');
                setStepState(stepTranscribe, '✓');
                setStepState(stepGenerate, '●');
                break;
        }

        const progressBarInner = document.getElementById('progressBarInner');
        const progressPercentage = document.getElementById('progressPercentage');
        const progressStatusMessage = document.getElementById('progressStatusMessage');

        if (progressBarInner) progressBarInner.style.width = `${pct}%`;
        if (progressPercentage) progressPercentage.textContent = `${pct}%`;
        if (progressStatusMessage) progressStatusMessage.textContent = msg;
    }

    function resetStepIcons() {
        ['stepDownload', 'stepConvert', 'stepTranscribe', 'stepGenerate'].forEach(id => {
            const el = document.getElementById(id);
            if (el) {
                el.style.color = 'var(--text-muted)';
                const icon = el.querySelector('.step-icon');
                if (icon) icon.textContent = '○';
            }
        });
    }

    function setStepState(el, symbol) {
        if (!el) return;
        el.style.color = symbol === '✓' ? '#22c55e' : 'var(--accent-color)';
        const icon = el.querySelector('.step-icon');
        if (icon) icon.textContent = symbol;
    }

    function showProgressCard(msg) {
        const progressCard = document.getElementById('progressCard');
        const completedCard = document.getElementById('completedCard');
        if (completedCard) completedCard.style.display = 'none';

        if (progressCard) {
            progressCard.style.display = 'block';
            updateProgressUI('Downloading');
        }
    }

    function hideProgressCard() {
        const progressCard = document.getElementById('progressCard');
        if (progressCard) progressCard.style.display = 'none';
    }

    function showCompletedCard(podcast) {
        const completedCard = document.getElementById('completedCard');
        const completedTitle = document.getElementById('completedTitle');
        const completedMeta = document.getElementById('completedMeta');
        const completedListenBtn = document.getElementById('completedListenBtn');

        if (completedCard && completedTitle && completedListenBtn) {
            completedTitle.textContent = podcast.title || 'Untitled Episode';
            if (completedMeta) {
                completedMeta.textContent = `${formatTime(podcast.duration)} · ${podcast.language?.toUpperCase() || 'AUTO'}`;
            }
            completedListenBtn.href = `/listen/${podcast.id}`;
            completedCard.style.display = 'block';
            completedCard.scrollIntoView({ behavior: 'smooth' });
        }
    }

    async function loadPodcastLibrary() {
        const libraryContainer = document.getElementById('libraryContainer');
        if (!libraryContainer) return;

        try {
            const res = await fetch('/api/podcast');
            if (!res.ok) return;
            const podcasts = await res.json();

            libraryContainer.innerHTML = '';

            if (podcasts.length === 0) {
                libraryContainer.innerHTML = '<p class="text-muted" style="text-align:center; padding:1.5rem;">No podcasts transcribed yet.</p>';
                return;
            }

            podcasts.forEach(p => {
                const item = document.createElement('div');
                item.className = 'podcast-item';

                item.innerHTML = `
                    <div class="podcast-item-info">
                        <h4>${escapeHtml(p.title || 'Untitled')}</h4>
                        <span>Status: <strong style="color:${p.status === 'Completed' ? '#22c55e' : 'var(--accent-color)'}">${p.status}</strong> • Duration: ${formatTime(p.duration)} • ${new Date(p.createdAt).toLocaleDateString()}</span>
                    </div>
                    <div class="podcast-item-actions">
                        ${p.status === 'Completed' ? `<a href="/listen/${p.id}" class="btn-primary" style="text-decoration:none; padding:0.4rem 0.8rem; font-size:0.85rem;">Listen & Read</a>` : ''}
                        <button class="btn-icon" onclick="window.deletePodcastApp('${p.id}')" title="Delete">🗑️</button>
                    </div>
                `;

                libraryContainer.appendChild(item);
            });
        } catch (e) {
            console.error('Error loading library:', e);
        }
    }

    window.deletePodcastApp = async (id) => {
        if (!confirm('Are you sure you want to delete this podcast and its transcript?')) return;
        try {
            const res = await fetch(`/api/podcast/${id}`, { method: 'DELETE' });
            if (res.ok) {
                loadPodcastLibrary();
            }
        } catch (e) {
            alert('Failed to delete podcast.');
        }
    };

    // ==========================================
    // LISTEN PAGE FUNCTIONS
    // ==========================================
    function initListenPage(podcastId) {
        let transcriptData = null;
        let wordsArray = []; // Sorted word list for Binary Search
        let activeWordIndex = -1;
        let activeSegmentIndex = -1;
        let isUserScrolling = false;
        let userScrollTimeout = null;

        let searchMatches = [];
        let currentMatchIndex = -1;

        const audioPlayer = document.getElementById('audioPlayer');
        if (!audioPlayer) return;

        const playPauseBtn = document.getElementById('playPauseBtn');
        const playIcon = document.getElementById('playIcon');
        const pauseIcon = document.getElementById('pauseIcon');
        const timelineSlider = document.getElementById('timelineSlider');
        const currentTimeText = document.getElementById('currentTimeText');
        const durationText = document.getElementById('durationText');
        const speedSelect = document.getElementById('speedSelect');
        const skipBackBtn = document.getElementById('skipBackBtn');
        const skipForwardBtn = document.getElementById('skipForwardBtn');

        const transcriptContainer = document.getElementById('transcriptContainer');
        const followBtn = document.getElementById('followBtn');
        const podcastTitleEl = document.getElementById('podcastTitle');
        const podcastMetaEl = document.getElementById('podcastMeta');

        const searchInput = document.getElementById('searchInput');
        const searchCounter = document.getElementById('searchCounter');
        const prevMatchBtn = document.getElementById('prevMatchBtn');
        const nextMatchBtn = document.getElementById('nextMatchBtn');

        loadPodcastData(podcastId);

        async function loadPodcastData(id) {
            try {
                const podcastRes = await fetch(`/api/podcast/${id}`);
                if (!podcastRes.ok) {
                    throw new Error('Podcast metadata not found.');
                }

                const podcast = await podcastRes.json();

                if (podcastTitleEl) podcastTitleEl.textContent = podcast.title || 'Untitled Podcast';
                if (podcastMetaEl) {
                    podcastMetaEl.textContent = `Status: ${podcast.status} • Duration: ${formatTime(podcast.duration)} • Language: ${podcast.language?.toUpperCase() || 'AUTO'}`;
                }

                if (podcast.status === 'Failed') {
                    if (transcriptContainer) {
                        transcriptContainer.innerHTML = `<div style="text-align:center; padding:2rem; color:#ef4444;">
                            <h4>Transcription Failed</h4>
                            <p style="margin-top:0.5rem; font-size:0.9rem;">${escapeHtml(podcast.errorMessage || 'Unknown error occurred during transcription.')}</p>
                            <a href="/import" class="btn-primary" style="display:inline-block; margin-top:1rem; text-decoration:none;">Return to Import</a>
                        </div>`;
                    }
                    return;
                }

                if (podcast.status !== 'Completed') {
                    if (transcriptContainer) {
                        transcriptContainer.innerHTML = `<div style="text-align:center; padding:2.5rem; color:var(--text-secondary);">
                            <div class="spinner" style="font-size:2rem; margin-bottom:1rem;">⏳</div>
                            <h4>Transcription in Progress (${podcast.status})</h4>
                            <p style="margin-top:0.5rem; font-size:0.9rem;">Whisper is generating timestamps and captions for this episode...</p>
                            <p style="font-size:0.8rem; color:var(--text-muted); margin-top:0.5rem;">This page will update automatically when ready.</p>
                        </div>`;
                    }

                    // Poll every 2 seconds until completed
                    setTimeout(() => loadPodcastData(id), 2000);
                    return;
                }

                const transcriptRes = await fetch(`/api/podcast/${id}/transcript`);
                if (!transcriptRes.ok) {
                    throw new Error('Transcript file not found.');
                }

                transcriptData = await transcriptRes.json();

                // Setup Audio Source safely
                if (audioPlayer) {
                    audioPlayer.src = `/api/podcast/${id}/audio`;
                    audioPlayer.load();
                }

                if (podcastMetaEl) {
                    podcastMetaEl.textContent = `Duration: ${formatTime(transcriptData.duration || podcast.duration)} • Language: ${transcriptData.language?.toUpperCase() || 'AUTO'}`;
                }

                // Render Continuous Transcript
                renderTranscriptGrid(transcriptData);

            } catch (err) {
                if (transcriptContainer) {
                    transcriptContainer.innerHTML = `<div style="text-align:center; padding:2rem; color:#ef4444;">
                        <h4>Error Loading Podcast</h4>
                        <p style="margin-top:0.5rem; font-size:0.9rem;">${escapeHtml(err.message)}</p>
                        <a href="/import" class="btn-primary" style="display:inline-block; margin-top:1rem; text-decoration:none;">Return to Import</a>
                    </div>`;
                }
            }
        }

        function renderTranscriptGrid(transcript) {
            transcriptContainer.innerHTML = '';
            wordsArray = [];
            activeWordIndex = -1;
            activeSegmentIndex = -1;

            if (!transcript || !transcript.segments) return;

            let globalWordIdx = 0;

            transcript.segments.forEach((seg, segIdx) => {
                const segDiv = document.createElement('div');
                segDiv.className = 'transcript-segment';
                segDiv.dataset.segmentIndex = segIdx;

                // Segment click-to-seek
                segDiv.addEventListener('click', (e) => {
                    if (e.target.classList.contains('transcript-word')) return; // handled by word click
                    seekAudioTo(seg.start);
                });

                // Left Timestamp Column
                const timeDiv = document.createElement('div');
                timeDiv.className = 'transcript-time';
                timeDiv.textContent = formatTime(seg.start);
                timeDiv.addEventListener('click', (e) => {
                    e.stopPropagation();
                    seekAudioTo(seg.start);
                });
                segDiv.appendChild(timeDiv);

                // Sentence Content Column
                const contentDiv = document.createElement('div');
                contentDiv.className = 'transcript-content';

                if (seg.words && seg.words.length > 0) {
                    seg.words.forEach(w => {
                        const wordSpan = document.createElement('span');
                        wordSpan.className = 'transcript-word';
                        wordSpan.textContent = w.text + ' ';
                        wordSpan.dataset.start = w.start;
                        wordSpan.dataset.end = w.end;
                        wordSpan.dataset.wordIndex = globalWordIdx;

                        wordSpan.addEventListener('click', (e) => {
                            e.stopPropagation();
                            seekAudioTo(w.start);
                        });

                        contentDiv.appendChild(wordSpan);

                        wordsArray.push({
                            start: w.start,
                            end: w.end,
                            text: w.text,
                            element: wordSpan,
                            segmentElement: segDiv,
                            segmentIndex: segIdx
                        });

                        globalWordIdx++;
                    });
                } else {
                    const segTextSpan = document.createElement('span');
                    segTextSpan.className = 'transcript-word';
                    segTextSpan.textContent = seg.text;
                    segTextSpan.dataset.start = seg.start;
                    segTextSpan.dataset.end = seg.end;
                    segTextSpan.addEventListener('click', (e) => {
                        e.stopPropagation();
                        seekAudioTo(seg.start);
                    });
                    contentDiv.appendChild(segTextSpan);

                    wordsArray.push({
                        start: seg.start,
                        end: seg.end,
                        text: seg.text,
                        element: segTextSpan,
                        segmentElement: segDiv,
                        segmentIndex: segIdx
                    });
                }

                segDiv.appendChild(contentDiv);
                transcriptContainer.appendChild(segDiv);
            });
        }

        // --- High Performance O(log N) Binary Search Timing ---
        audioPlayer.addEventListener('timeupdate', () => {
            const currentTime = audioPlayer.currentTime;

            if (!timelineSlider.isDragging) {
                timelineSlider.value = currentTime;
                currentTimeText.textContent = formatTime(currentTime);
            }

            if (wordsArray.length === 0) return;

            const newWordIndex = findActiveWordBinarySearch(currentTime);

            if (newWordIndex !== activeWordIndex) {
                // Remove previous word outline highlight
                if (activeWordIndex >= 0 && activeWordIndex < wordsArray.length) {
                    wordsArray[activeWordIndex].element.classList.remove('active');
                }

                // Highlight new active word
                if (newWordIndex >= 0) {
                    const item = wordsArray[newWordIndex];
                    item.element.classList.add('active');

                    // Active Segment highlight & smooth scroll
                    if (item.segmentIndex !== activeSegmentIndex) {
                        if (activeSegmentIndex >= 0 && wordsArray[activeWordIndex]) {
                            wordsArray[activeWordIndex].segmentElement.classList.remove('active');
                        }
                        item.segmentElement.classList.add('active');
                        activeSegmentIndex = item.segmentIndex;

                        if (!isUserScrolling) {
                            item.segmentElement.scrollIntoView({
                                behavior: 'smooth',
                                block: 'center'
                            });
                        }
                    }
                }
                activeWordIndex = newWordIndex;
            }
        });

        function findActiveWordBinarySearch(time) {
            let low = 0;
            let high = wordsArray.length - 1;

            while (low <= high) {
                const mid = Math.floor((low + high) / 2);
                const word = wordsArray[mid];

                if (time >= word.start && time <= word.end) {
                    return mid;
                } else if (time < word.start) {
                    high = mid - 1;
                } else {
                    low = mid + 1;
                }
            }

            if (high >= 0 && high < wordsArray.length) {
                const candidate = wordsArray[high];
                if (time >= candidate.start && time <= candidate.end + 0.5) {
                    return high;
                }
            }
            return -1;
        }

        function seekAudioTo(seconds) {
            audioPlayer.currentTime = seconds;
            audioPlayer.play();
            isUserScrolling = false;
            if (followBtn) followBtn.style.display = 'none';
        }

        // --- Manual Scroll Detection ---
        window.addEventListener('wheel', handleUserScroll, { passive: true });
        window.addEventListener('touchmove', handleUserScroll, { passive: true });

        function handleUserScroll() {
            isUserScrolling = true;
            if (followBtn) followBtn.style.display = 'flex';

            clearTimeout(userScrollTimeout);
            userScrollTimeout = setTimeout(() => {}, 5000);
        }

        if (followBtn) {
            followBtn.addEventListener('click', () => {
                isUserScrolling = false;
                followBtn.style.display = 'none';
                if (activeSegmentIndex >= 0 && wordsArray[activeWordIndex]) {
                    wordsArray[activeWordIndex].segmentElement.scrollIntoView({
                        behavior: 'smooth',
                        block: 'center'
                    });
                }
            });
        }

        // --- Audio Player Controls ---
        audioPlayer.addEventListener('loadedmetadata', () => {
            durationText.textContent = formatTime(audioPlayer.duration);
            timelineSlider.max = audioPlayer.duration;
        });

        if (playPauseBtn) playPauseBtn.addEventListener('click', togglePlayPause);

        function togglePlayPause() {
            if (audioPlayer.paused) {
                audioPlayer.play();
            } else {
                audioPlayer.pause();
            }
        }

        audioPlayer.addEventListener('play', () => {
            if (playIcon) playIcon.style.display = 'none';
            if (pauseIcon) pauseIcon.style.display = 'block';
        });

        audioPlayer.addEventListener('pause', () => {
            if (playIcon) playIcon.style.display = 'block';
            if (pauseIcon) pauseIcon.style.display = 'none';
        });

        if (skipBackBtn) {
            skipBackBtn.addEventListener('click', () => {
                audioPlayer.currentTime = Math.max(0, audioPlayer.currentTime - 10);
            });
        }

        if (skipForwardBtn) {
            skipForwardBtn.addEventListener('click', () => {
                audioPlayer.currentTime = Math.min(audioPlayer.duration, audioPlayer.currentTime + 10);
            });
        }

        if (speedSelect) {
            speedSelect.addEventListener('change', () => {
                audioPlayer.playbackRate = parseFloat(speedSelect.value);
            });
        }

        if (timelineSlider) {
            timelineSlider.addEventListener('input', () => {
                timelineSlider.isDragging = true;
                currentTimeText.textContent = formatTime(timelineSlider.value);
            });

            timelineSlider.addEventListener('change', () => {
                audioPlayer.currentTime = timelineSlider.value;
                timelineSlider.isDragging = false;
            });
        }

        // Keyboard Shortcuts
        document.addEventListener('keydown', (e) => {
            if (['INPUT', 'TEXTAREA', 'SELECT'].includes(document.activeElement.tagName)) return;

            if (e.code === 'Space') {
                e.preventDefault();
                togglePlayPause();
            } else if (e.code === 'ArrowLeft') {
                e.preventDefault();
                audioPlayer.currentTime = Math.max(0, audioPlayer.currentTime - 5);
            } else if (e.code === 'ArrowRight') {
                e.preventDefault();
                audioPlayer.currentTime = Math.min(audioPlayer.duration, audioPlayer.currentTime + 5);
            }
        });

        // --- Search Feature ---
        if (searchInput) {
            searchInput.addEventListener('input', () => {
                const query = searchInput.value.trim().toLowerCase();
                clearSearchHighlights();

                if (!query) {
                    searchCounter.textContent = '';
                    searchMatches = [];
                    currentMatchIndex = -1;
                    return;
                }

                searchMatches = wordsArray.filter(w => w.text.toLowerCase().includes(query));
                searchMatches.forEach(match => match.element.classList.add('search-match'));

                searchCounter.textContent = `${searchMatches.length} matches`;

                if (searchMatches.length > 0) {
                    currentMatchIndex = 0;
                    highlightCurrentSearchMatch();
                }
            });
        }

        if (nextMatchBtn) {
            nextMatchBtn.addEventListener('click', () => {
                if (searchMatches.length === 0) return;
                currentMatchIndex = (currentMatchIndex + 1) % searchMatches.length;
                highlightCurrentSearchMatch();
            });
        }

        if (prevMatchBtn) {
            prevMatchBtn.addEventListener('click', () => {
                if (searchMatches.length === 0) return;
                currentMatchIndex = (currentMatchIndex - 1 + searchMatches.length) % searchMatches.length;
                highlightCurrentSearchMatch();
            });
        }

        function clearSearchHighlights() {
            wordsArray.forEach(w => w.element.classList.remove('search-match', 'current-match'));
        }

        function highlightCurrentSearchMatch() {
            wordsArray.forEach(w => w.element.classList.remove('current-match'));
            if (currentMatchIndex >= 0 && currentMatchIndex < searchMatches.length) {
                const match = searchMatches[currentMatchIndex];
                match.element.classList.add('current-match');
                match.element.scrollIntoView({ behavior: 'smooth', block: 'center' });
                searchCounter.textContent = `${currentMatchIndex + 1} of ${searchMatches.length}`;
                seekAudioTo(match.start);
            }
        }
    }

    // --- SYSTEM HEALTH CHECK ---
    async function checkSystemHealth() {
        const transDot = document.getElementById('transcriberDot');
        const transStatus = document.getElementById('transcriberStatus');
        const ffmpegDot = document.getElementById('ffmpegDot');

        try {
            const res = await fetch('/api/health');
            if (!res.ok) return;
            const health = await res.json();

            if (transDot && transStatus) {
                if (health.transcriber === 'online') {
                    transDot.className = 'dot online';
                    transStatus.textContent = 'Transcriber online';
                } else {
                    transDot.className = 'dot offline';
                    transStatus.textContent = 'Transcriber offline';
                }
            }

            if (ffmpegDot) {
                ffmpegDot.className = health.ffmpeg === 'available' ? 'dot online' : 'dot offline';
            }
        } catch (e) {
            if (transDot) transDot.className = 'dot offline';
        }
    }

    // --- HELPER FUNCTIONS ---
    function formatTime(seconds) {
        if (isNaN(seconds) || seconds < 0) return '00:00';
        const mins = Math.floor(seconds / 60);
        const secs = Math.floor(seconds % 60);
        return `${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
    }

    function escapeHtml(str) {
        return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }
});
