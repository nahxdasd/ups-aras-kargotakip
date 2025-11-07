console.log('🚀 Script.js loaded successfully!');
const apiUrl = '/api/kargo';

// Talep ID'sini 4me linki olarak oluştur
function get4meRequestUrl(talepId) {
    if (!talepId) return '#';
    return `https://gratis-it.4me.com/requests/${encodeURIComponent(talepId)}`;
}

// Yeni sekmede link açma yardımcı fonksiyonu
async function openInNewTab(url) {
    if (url && url !== '#') {
        return window.open(url, '_blank');
    }
    return null;
}

// Linkleri gruplar halinde açma fonksiyonu
async function openLinksInBatches(links, batchSize = 5, delayBetweenBatches = 1000) {
    showStatusModal('Linkler Açılıyor', 'Linkleri gruplar halinde açıyorum...');
    
    const totalLinks = links.length;
    let processedLinks = 0;
    
    for (let i = 0; i < links.length; i += batchSize) {
        const batch = links.slice(i, i + batchSize);
        const windows = await Promise.all(batch.map(link => openInNewTab(link)));
        
        processedLinks += batch.length;
        const progress = Math.round((processedLinks / totalLinks) * 100);
        
        showStatusModal(
            'Linkler Açılıyor',
            `İşlenen: ${processedLinks}/${totalLinks} (${progress}%)\nBir sonraki grup için bekleniyor...`
        );
        
        if (i + batchSize < links.length) {
            await new Promise(resolve => setTimeout(resolve, delayBetweenBatches));
        }
    }
    
    setTimeout(() => {
        hideStatusModal();
        showStatusModal('Tamamlandı', `${totalLinks} link başarıyla açıldı.`);
        setTimeout(hideStatusModal, 2000);
    }, 1000);
}

// Kargo linklerini gruplar halinde açma fonksiyonları
let currentTrackingIndex = {};  // Her durum için ayrı index tutuyoruz
let completedCategories = new Set(); // Tamamlanan kategorileri takip etmek için

async function openAllTrackingLinks(status) {
    // Eğer bu kategori tamamlanmışsa, işlemi durdur
    if (completedCategories.has(`tracking-${status}`)) {
        showStatusModal('Bilgi', 'Bu kategorideki tüm linkler zaten açıldı. Yeni linkler için sayfayı yenileyin.');
        setTimeout(hideStatusModal, 3000);
        return;
    }

    const kargos = allKargos.filter(k => {
        if (status === 'delivered') return k.durum === 'Teslim Edildi';
        if (status === 'pending') return k.durum !== 'Teslim Edildi';
        return true;
    });
    
    const validLinks = kargos
        .map(kargo => getTrackingUrl(kargo.takipNo))
        .filter(url => url !== '#');
    
    if (validLinks.length === 0) {
        showStatusModal('Bilgi', 'Açılacak kargo linki bulunamadı.');
        setTimeout(hideStatusModal, 2000);
        return;
    }

    // Her durum için index yoksa 0'dan başlat
    if (!(status in currentTrackingIndex)) {
        currentTrackingIndex[status] = 0;
    }

    const start = currentTrackingIndex[status];
    const end = Math.min(start + 5, validLinks.length);
    const currentBatch = validLinks.slice(start, end);
    const remainingCount = validLinks.length - end;

    // Eğer bu son grup ise
    if (end >= validLinks.length) {
        await openLinksInBatches(currentBatch, currentBatch.length, 1000);
        showStatusModal('Tamamlandı', `Bu kategorideki tüm linkler açıldı! (${validLinks.length} adet)`);
        setTimeout(hideStatusModal, 3000);
        
        // Kategoriyi tamamlandı olarak işaretle ve butonu devre dışı bırak
        completedCategories.add(`tracking-${status}`);
        const buttonId = status === 'delivered' ? 'openDeliveredTrackingLinks' : 'openPendingTrackingLinks';
        document.getElementById(buttonId).disabled = true;
        document.getElementById(buttonId).classList.add('opacity-50', 'cursor-not-allowed');
    } else {
        // Devam eden gruplar için kalan sayıyı göster
        currentTrackingIndex[status] = end;
        await openLinksInBatches(currentBatch, currentBatch.length, 1000);
        showStatusModal('Devam Ediyor', `${remainingCount} link daha var. Yeni grup için tekrar tıklayın.`);
        setTimeout(hideStatusModal, 3000);
    }
}

// Talep linklerini gruplar halinde açma fonksiyonları
let currentRequestIndex = {};  // Her durum için ayrı index tutuyoruz

async function openAllRequestLinks(status) {
    // Eğer bu kategori tamamlanmışsa, işlemi durdur
    if (completedCategories.has(`request-${status}`)) {
        showStatusModal('Bilgi', 'Bu kategorideki tüm talep linkleri zaten açıldı. Yeni linkler için sayfayı yenileyin.');
        setTimeout(hideStatusModal, 3000);
        return;
    }

    const kargos = allKargos.filter(k => {
        if (status === 'delivered') return k.durum === 'Teslim Edildi';
        if (status === 'pending') return k.durum !== 'Teslim Edildi';
        return true;
    });
    
    const validLinks = kargos
        .filter(kargo => kargo.talepId)
        .map(kargo => get4meRequestUrl(kargo.talepId))
        .filter(url => url !== '#');
    
    if (validLinks.length === 0) {
        showStatusModal('Bilgi', 'Açılacak talep linki bulunamadı.');
        setTimeout(hideStatusModal, 2000);
        return;
    }

    // Her durum için index yoksa 0'dan başlat
    if (!(status in currentRequestIndex)) {
        currentRequestIndex[status] = 0;
    }

    const start = currentRequestIndex[status];
    const end = Math.min(start + 5, validLinks.length);
    const currentBatch = validLinks.slice(start, end);
    const remainingCount = validLinks.length - end;

    // Eğer bu son grup ise
    if (end >= validLinks.length) {
        await openLinksInBatches(currentBatch, currentBatch.length, 1000);
        showStatusModal('Tamamlandı', `Bu kategorideki tüm talep linkleri açıldı! (${validLinks.length} adet)`);
        setTimeout(hideStatusModal, 3000);
        
        // Kategoriyi tamamlandı olarak işaretle ve butonu devre dışı bırak
        completedCategories.add(`request-${status}`);
        const buttonId = status === 'delivered' ? 'openDeliveredRequestLinks' : 'openPendingRequestLinks';
        document.getElementById(buttonId).disabled = true;
        document.getElementById(buttonId).classList.add('opacity-50', 'cursor-not-allowed');
    } else {
        // Devam eden gruplar için kalan sayıyı göster
        currentRequestIndex[status] = end;
        await openLinksInBatches(currentBatch, currentBatch.length, 1000);
        showStatusModal('Devam Ediyor', `${remainingCount} talep linki daha var. Yeni grup için tekrar tıklayın.`);
        setTimeout(hideStatusModal, 3000);
    }
}

// Global variables
let allKargos = [];
let filteredKargos = [];
let currentFilter = 'all';
let currentSort = { field: null, direction: 'asc' };

// Tracking helper: decide carrier and return proper tracking URL
function getTrackingUrl(takipNo) {
    if (!takipNo) return '#';
    const t = takipNo.trim();

    // UPS common format: starts with 1Z (case-insensitive) or contains letters mixed with numbers
    if (/^1Z/i.test(t) || /[A-Za-z]/.test(t)) {
        // New UPS tracking URL (works for TR locale)
        return `https://www.ups.com/track?loc=tr_TR&tracknum=${encodeURIComponent(t)}`;
    }

    // Aras Kargo: typically numeric (9-14 digits) — use Aras tracking page
    if (/^\d{9,14}$/.test(t)) {

        return `https://kargotakip.araskargo.com.tr/mainpage.aspx?code=${encodeURIComponent(t)}`;
    }

    // Fallback to UPS tracking
    return `https://www.ups.com/track?loc=tr_TR&tracknum=${encodeURIComponent(t)}`;
}

// 2FA Session variables
let currentSessionId = null;
let currentTwoFactorCode = null;

function showLoading() {
    document.getElementById('loadingOverlay').style.display = 'flex';
}

function hideLoading() {
    document.getElementById('loadingOverlay').style.display = 'none';
}

// Status Modal Functions
function showStatusModal(message, details = '') {
    document.getElementById('statusMessage').textContent = message;
    document.getElementById('statusDetails').textContent = details;
    document.getElementById('statusModal').classList.remove('hidden');
}

function hideStatusModal() {
    document.getElementById('statusModal').classList.add('hidden');
}

function updateStatusMessage(message, details = '') {
    document.getElementById('statusMessage').textContent = message;
    document.getElementById('statusDetails').textContent = details;
}

// Status polling system
let statusPollingInterval = null;

function startStatusPolling(sessionId) {
    console.log('Status polling başlatıldı:', sessionId);
    
    // Mevcut polling'i durdur
    if (statusPollingInterval) {
        clearInterval(statusPollingInterval);
    }
    
    // Her 2 saniyede bir status kontrol et
    statusPollingInterval = setInterval(async () => {
        try {
            const response = await fetch(`${apiUrl}/status/${sessionId}`);
            const statusData = await response.json();
            
            console.log('Status güncellendi:', statusData);
            
            if (statusData.status) {
                updateStatusMessage(statusData.status, 'İşlem devam ediyor...');
            }
            
            // İşlem tamamlandıysa polling'i durdur
            if (statusData.isComplete) {
                clearInterval(statusPollingInterval);
                statusPollingInterval = null;
            }
        } catch (error) {
            console.error('Status polling hatası:', error);
        }
    }, 2000);
}

function stopStatusPolling() {
    if (statusPollingInterval) {
        clearInterval(statusPollingInterval);
        statusPollingInterval = null;
        console.log('Status polling durduruldu');
    }
}

// Fetch kargos from API
async function fetchKargolar() {
    showLoading();
    try {
        const res = await fetch(apiUrl);
        const data = await res.json();
        allKargos = data;
        filteredKargos = [...data];
        updateTable();
        updateFilterCounts();
        
        // Yeni veri yüklendiğinde tamamlanan kategorileri sıfırla ve butonları aktif et
        completedCategories.clear();
        ['openDeliveredTrackingLinks', 'openPendingTrackingLinks', 
         'openDeliveredRequestLinks', 'openPendingRequestLinks'].forEach(buttonId => {
            const button = document.getElementById(buttonId);
            if (button) {
                button.disabled = false;
                button.classList.remove('opacity-50', 'cursor-not-allowed');
            }
        });
    } catch (error) {
        alert('Kargolar yüklenirken bir hata oluştu: ' + error.message);
    } finally {
        hideLoading();
    }
}

// Update filter counts
function updateFilterCounts() {
    const delivered = allKargos.filter(k => k.durum === 'Teslim Edildi').length;
    const pending = allKargos.filter(k => k.durum === 'Beklemede').length;
    
    document.querySelector('#filterAll .count').textContent = allKargos.length;
    document.querySelector('#filterDelivered .count').textContent = delivered;
    document.querySelector('#filterPending .count').textContent = pending;
}

// Filter functions
function filterKargos(filterType) {
    console.log('Filtering by:', filterType);
    currentFilter = filterType;
    
    // Update active button
    document.querySelectorAll('.filter-btn').forEach(btn => btn.classList.remove('active'));
    
    let activeButtonId = '';
    switch(filterType) {
        case 'all':
            activeButtonId = 'filterAll';
            break;
        case 'delivered':
            activeButtonId = 'filterDelivered';
            break;
        case 'pending':
            activeButtonId = 'filterPending';
            break;
    }
    
    const activeButton = document.getElementById(activeButtonId);
    if (activeButton) {
        activeButton.classList.add('active');
    }
    
    // Apply filter
    switch(filterType) {
        case 'delivered':
            filteredKargos = allKargos.filter(k => k.durum === 'Teslim Edildi');
            break;
        case 'pending':
            filteredKargos = allKargos.filter(k => k.durum === 'Beklemede');
            break;
        default:
            filteredKargos = [...allKargos];
    }
    
    // Apply current search if any
    const searchTerm = document.getElementById('searchInput').value.toLowerCase();
    if (searchTerm) {
        searchKargos(searchTerm);
    } else {
        updateTable();
    }
}

// Search function
function searchKargos(searchTerm) {
    if (!searchTerm) {
        filteredKargos = currentFilter === 'all' ? [...allKargos] : 
                        currentFilter === 'delivered' ? allKargos.filter(k => k.durum === 'Teslim Edildi') :
                        allKargos.filter(k => k.durum === 'Beklemede');
    } else {
        const baseData = currentFilter === 'all' ? allKargos : 
                        currentFilter === 'delivered' ? allKargos.filter(k => k.durum === 'Teslim Edildi') :
                        allKargos.filter(k => k.durum === 'Beklemede');
        
        filteredKargos = baseData.filter(kargo => 
            kargo.takipNo.toLowerCase().includes(searchTerm) ||
            (kargo.magazaId && kargo.magazaId.toLowerCase().includes(searchTerm)) ||
            (kargo.talepId && kargo.talepId.toLowerCase().includes(searchTerm)) ||
            (kargo.talepAdi && kargo.talepAdi.toLowerCase().includes(searchTerm))
        );
    }
    updateTable();
}

// Sort function
function sortKargos(field, direction = null) {
    if (direction === null) {
        // Toggle direction if same field
        if (currentSort.field === field) {
            direction = currentSort.direction === 'asc' ? 'desc' : 'asc';
        } else {
            direction = 'asc';
        }
    }
    
    currentSort = { field, direction };
    
    // Update sort indicators
    document.querySelectorAll('.sort-indicator').forEach(indicator => {
        indicator.textContent = '↕️';
    });
    
    const currentHeader = document.querySelector(`[data-sort="${field}"] .sort-indicator`);
    if (currentHeader) {
        currentHeader.textContent = direction === 'asc' ? '↑' : '↓';
    }
    
    // Sort the data
    filteredKargos.sort((a, b) => {
        let aVal = a[field] || '';
        let bVal = b[field] || '';
        
        // Special handling for dates
        if (field === 'sonGuncelleme') {
            aVal = new Date(aVal);
            bVal = new Date(bVal);
        }
        
        if (aVal < bVal) return direction === 'asc' ? -1 : 1;
        if (aVal > bVal) return direction === 'asc' ? 1 : -1;
        return 0;
    });
    
    updateTable();
}

// Special sort functions
function sortByStatus(deliveredFirst = true) {
    filteredKargos.sort((a, b) => {
        const aDelivered = a.durum === 'Teslim Edildi';
        const bDelivered = b.durum === 'Teslim Edildi';
        
        if (deliveredFirst) {
            if (aDelivered && !bDelivered) return -1;
            if (!aDelivered && bDelivered) return 1;
        } else {
            if (!aDelivered && bDelivered) return -1;
            if (aDelivered && !bDelivered) return 1;
        }
        
        // Secondary sort by date
        return new Date(b.sonGuncelleme) - new Date(a.sonGuncelleme);
    });
    
    updateTable();
}

// Update table display
function updateTable() {
    const tbody = document.querySelector("#kargoTable tbody");
    tbody.innerHTML = "";
    
    filteredKargos.forEach((k, index) => {
        const row = document.createElement("tr");
        row.className = `hover:bg-primary/5 dark:hover:bg-primary-dark/10 transition-all duration-200 ${index % 2 === 0 ? 'bg-white/30 dark:bg-gray-900/30' : 'bg-white/20 dark:bg-gray-800/20'}`;
        row.innerHTML = `
            <td class="py-4 px-4 font-medium">
                <a href="${getTrackingUrl(k.takipNo)}" target="_blank" rel="noopener noreferrer"
                   class="text-primary hover:text-primary-dark transition-colors font-mono text-sm bg-white/50 dark:bg-gray-800/50 px-3 py-1.5 rounded-lg shadow-sm hover:shadow">
                   ${k.takipNo}
                </a>
            </td>
            <td class="py-4 px-4 text-gray-700 dark:text-gray-300">
                <span class="bg-white/50 dark:bg-gray-800/50 px-3 py-1.5 rounded-lg">
                    ${k.magazaId || '-'}
                </span>
            </td>
            <td class="py-4 px-4">
                <a href="https://gratis-it.4me.com/requests/${k.talepId}" target="_blank" 
                   class="text-primary hover:text-primary-dark transition-colors font-mono text-sm bg-white/50 dark:bg-gray-800/50 px-3 py-1.5 rounded-lg shadow-sm hover:shadow">
                   ${k.talepId}
                </a>
            </td>
            <td class="py-4 px-4">
                <span class="bg-white/50 dark:bg-gray-800/50 px-3 py-1.5 rounded-lg inline-block max-w-xs truncate" title="${k.talepAdi || ''}">
                    ${k.talepAdi || '-'}
                </span>
            </td>
            <td class="py-4 px-4">
                <span class="inline-flex items-center px-3 py-1.5 rounded-lg text-sm font-medium shadow-sm
                    ${k.durum === 'Teslim Edildi'
                        ? 'bg-green-400/20 text-green-700 dark:text-green-400'
                        : 'bg-yellow-400/20 text-yellow-700 dark:text-yellow-400'}">
                    ${k.durum === 'Teslim Edildi' ? '✅' : '⏳'} ${k.durum || 'Bilinmiyor'}
                </span>
            </td>
            <td class="py-4 px-4 text-center">
                <button onclick="deleteKargo('${k.takipNo}')" 
                        class="bg-gradient-to-r from-rose-500 to-pink-600 text-white px-4 py-2 rounded-lg text-sm font-medium transition-all duration-200 transform hover:scale-105 shadow-lg hover:shadow-xl">
                    🗑️
                </button>
            </td>
        `;
        tbody.appendChild(row);
    });

    // Update count display
    const countElement = document.getElementById("talepSayisi");
    if (countElement) {
        countElement.innerHTML = `📊 Görüntülenen: <span class="text-primary">${filteredKargos.length}</span> / Toplam: <span class="text-primary">${allKargos.length}</span>`;
    }
}

// Delete single kargo
async function deleteKargo(takipNo) {
    showLoading();
    try {
        const response = await fetch(`${apiUrl}/${takipNo}`, { method: "DELETE" });
        if (!response.ok) {
            const errorText = await response.text();
            throw new Error(errorText);
        }
        await fetchKargolar();
    } catch (error) {
        alert('Kargo silinirken bir hata oluştu: ' + error.message);
    } finally {
        hideLoading();
    }
}

// Add kargo form handler
document.getElementById("kargoForm").addEventListener("submit", async (e) => {
    e.preventDefault();
    showLoading();
    // Build payload matching backend's JSON property names
    const kargo = {
        takipNo: document.getElementById("takipNo").value || "",
        magazaId: document.getElementById("magazaID").value || "",
        talepId: document.getElementById("talepID").value || "",
        talepAdi: "",
        durum: "Beklemede",
        ongorulenTeslimat: "-",
        sonGuncelleme: new Date().toISOString()
    };
    try {
        const response = await fetch(apiUrl, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(kargo)
        });
        
        if (!response.ok) {
            if (response.status === 409) {
                alert("Bu takip numarası zaten sistemde kayıtlı.");
            } else {
                alert("Bir hata oluştu.");
            }
        } else {
            fetchKargolar();
            // Clear form
            document.getElementById("kargoForm").reset();
        }
    } catch (error) {
        alert('Kargo eklenirken bir hata oluştu: ' + error.message);
    } finally {
        hideLoading();
    }
});

// Helper to update table with data (used by 2FA flow)
function updateKargoTable(data) {
    if (!Array.isArray(data)) return;
    allKargos = data;
    filteredKargos = [...data];
    updateTable();
    updateFilterCounts();
}

// Wait for DOM to be fully loaded
document.addEventListener('DOMContentLoaded', function() {
    console.log('🎯 DOM loaded, setting up event listeners...');
    console.log('🔍 Looking for loadFrom4me button...');
    
    // Toplu link açma butonları
    document.getElementById('openDeliveredTrackingLinks')?.addEventListener('click', () => {
        console.log('Teslim edilenlerin kargo linkleri açılıyor...');
        openAllTrackingLinks('delivered');
    });

    document.getElementById('openPendingTrackingLinks')?.addEventListener('click', () => {
        console.log('Bekleyenlerin kargo linkleri açılıyor...');
        openAllTrackingLinks('pending');
    });

    document.getElementById('openDeliveredRequestLinks')?.addEventListener('click', () => {
        console.log('Teslim edilenlerin talep linkleri açılıyor...');
        openAllRequestLinks('delivered');
    });

    document.getElementById('openPendingRequestLinks')?.addEventListener('click', () => {
        console.log('Bekleyenlerin talep linkleri açılıyor...');
        openAllRequestLinks('pending');
    });
    
    // Filter buttons
    const filterAll = document.getElementById('filterAll');
    const filterDelivered = document.getElementById('filterDelivered');
    const filterPending = document.getElementById('filterPending');
    
    if (filterAll) {
        filterAll.addEventListener('click', () => {
            console.log('Filter All clicked');
            filterKargos('all');
        });
    }
    
    if (filterDelivered) {
        filterDelivered.addEventListener('click', () => {
            console.log('Filter Delivered clicked');
            filterKargos('delivered');
        });
    }
    
    if (filterPending) {
        filterPending.addEventListener('click', () => {
            console.log('Filter Pending clicked');
            filterKargos('pending');
        });
    }
    
    // Sort buttons
    const sortDeliveredFirst = document.getElementById('sortDeliveredFirst');
    const sortPendingFirst = document.getElementById('sortPendingFirst');
    
    if (sortDeliveredFirst) {
        sortDeliveredFirst.addEventListener('click', () => {
            console.log('Sort Delivered First clicked');
            sortByStatus(true);
        });
    }
    
    if (sortPendingFirst) {
        sortPendingFirst.addEventListener('click', () => {
            console.log('Sort Pending First clicked');
            sortByStatus(false);
        });
    }
    
    // Search input
    const searchInput = document.getElementById('searchInput');
    if (searchInput) {
        searchInput.addEventListener('input', (e) => {
            console.log('Search input:', e.target.value);
            searchKargos(e.target.value.toLowerCase());
        });
    }
    
    // Column header sorting
    document.querySelectorAll('.sortable').forEach(header => {
        header.addEventListener('click', () => {
            const field = header.getAttribute('data-sort');
            console.log('Sorting by:', field);
            sortKargos(field);
        });
    });
    
    // Load from 4me button - Show login modal
    const loadBtn = document.getElementById("loadFrom4me");
    console.log('Load button found:', loadBtn);
    if (loadBtn) {
        loadBtn.addEventListener("click", () => {
            console.log('Load button clicked!');
            showLoginModal();
        });
    } else {
        console.error('Load from 4me button not found!');
    }

    // Delete all button
    const deleteAllButton = document.getElementById('deleteAllKargos');
    if (deleteAllButton) {
        deleteAllButton.addEventListener('click', async () => {
            if (confirm("Tüm kargoları silmek istediğinize emin misiniz?")) {
                showLoading();
                try {
                    const response = await fetch('/api/kargo/delete-all', {
                        method: 'DELETE'
                    });
                    if (!response.ok) {
                        const errorText = await response.text();
                        throw new Error(errorText);
                    }
                    await fetchKargolar();
                } catch (error) {
                    alert('Tüm kargolar silinirken bir hata oluştu: ' + error.message);
                } finally {
                    hideLoading();
                }
            }
        });
    }

    // Update button
    const updateButton = document.getElementById('updateKargos');
    let isUpdating = false;

    updateButton.addEventListener('click', async function() {
        if (isUpdating) return;
        
        isUpdating = true;
        updateButton.disabled = true;
        showStatusModal('🔄 Kargolar Güncelleniyor', 'Kargo durumları kontrol ediliyor...');

        try {
            const response = await fetch('/api/kargo/update-all', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                }
            });

            // Önce yanıtın text içeriğini alalım
            const textResponse = await response.text();
            let result;
            
            try {
                // Text içeriğini JSON'a çevirmeyi deneyelim
                result = JSON.parse(textResponse);
                
                if (result.success) {
                    updateStatusMessage('✅ Kargolar güncellendi!', 'Veriler yenileniyor...');
                    await fetchKargolar();
                    setTimeout(() => {
                        hideStatusModal();
                    }, 2000);
                } else {
                    hideStatusModal();
                    alert('Hata: ' + result.message);
                }
            } catch (jsonError) {
                // Eğer JSON parse edilemezse, güncelleme devam ediyor olabilir
                console.log('API yanıtı JSON formatında değil, güncelleme devam ediyor olabilir');
                updateStatusMessage('🔄 Güncelleme devam ediyor...', 'Kargo durumları kontrol ediliyor...');
                // 5 saniye sonra verileri yeniden yükleyelim
                setTimeout(async () => {
                    await fetchKargolar();
                    hideStatusModal();
                }, 5000);
            }
        } catch (error) {
            hideStatusModal();
            alert('Bir hata oluştu: ' + error.message);
        } finally {
            isUpdating = false;
            updateButton.disabled = false;
        }
    });

    // Login modal event listeners
    setupLoginModal();
    
    // 2FA modal event listeners


    // Initial load
    fetchKargolar();
});

// Modal management functions
function showLoginModal() {
    console.log('showLoginModal called');
    const modal = document.getElementById('loginModal');
    console.log('Login modal element:', modal);
    if (modal) {
        modal.classList.remove('hidden');
        const emailInput = document.getElementById('loginEmail');
        if (emailInput) {
            emailInput.focus();
        }
        console.log('Login modal should be visible now');
    } else {
        console.error('Login modal not found!');
    }
}

function hideLoginModal() {
    document.getElementById('loginModal').classList.add('hidden');
    document.getElementById('loginForm').reset();
}

function showTwoFactorModal(code) {
    const codeEl = document.getElementById('displayedCode');
    const modalEl = document.getElementById('twoFactorModal');
    if (codeEl) codeEl.textContent = code;
    if (modalEl) modalEl.classList.remove('hidden');
    currentTwoFactorCode = code;
}

function hideTwoFactorModal() {
    const codeEl = document.getElementById('displayedCode');
    const modalEl = document.getElementById('twoFactorModal');
    if (modalEl) modalEl.classList.add('hidden');
    if (codeEl) codeEl.textContent = '-';
    currentTwoFactorCode = null;
}

// 2FA Alert sonrası otomatik onaylama
async function handleTwoFactorConfirmation() {
    try {
        showStatusModal('2FA kodu onaylandı! 🎉', 'Oturum onayı bekleniyor...');
        
        // Status polling'i yeniden başlat
        startStatusPolling(currentSessionId);
        
        console.log('2FA verify isteği gönderiliyor:', {
            sessionId: currentSessionId,
            code: currentTwoFactorCode,
            url: `${apiUrl}/verify-2fa`,
            sessionIdType: typeof currentSessionId,
            codeType: typeof currentTwoFactorCode,
            sessionIdLength: currentSessionId ? currentSessionId.length : 0,
            codeLength: currentTwoFactorCode ? currentTwoFactorCode.length : 0
        });
        
        const response = await fetch(`${apiUrl}/verify-2fa`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                sessionId: currentSessionId,
                code: currentTwoFactorCode
            })
        });
        
        console.log('2FA verify response:', response.status, response.statusText);

        const result = await response.json();
        
        if (result.success) {
            updateStatusMessage('✅ Veri yükleme tamamlandı!');
            
            // Eğer result.data varsa direkt tabloyu güncelle
            if (result.data && result.data.length > 0) {
                setTimeout(() => {
                    stopStatusPolling();
                    hideStatusModal();
                    updateKargoTable(result.data);
                    alert(`✅ Başarılı! ${result.data.length} kargo başarıyla yüklendi.`);
                }, 2000);
            } else {
                // Yoksa polling devam etsin ve 30 saniye sonra fetchKargolar çağır
                setTimeout(async () => {
                    stopStatusPolling();
                    hideStatusModal();
                    await fetchKargolar();
                }, 30000);
            }
        } else {
            stopStatusPolling();
            hideStatusModal();
            alert('2FA doğrulama hatası: ' + result.message);
        }
    } catch (error) {
        console.error('2FA doğrulama hatası:', error);
        stopStatusPolling();
        hideStatusModal();
        alert('2FA doğrulama sırasında bir hata oluştu: ' + error.message);
    }
}

// Login modal setup
function setupLoginModal() {
    // Login form submit
    document.getElementById('loginForm').addEventListener('submit', async (e) => {
        e.preventDefault();
        
        const email = document.getElementById('loginEmail').value;
        const password = document.getElementById('loginPassword').value;
        
        if (!email || !password) {
            alert('Email ve şifre gereklidir!');
            return;
        }

        // Login modal'ını kapat ve status modal'ı göster
        hideLoginModal();
        showStatusModal('🚀 Giriş işlemi başlatılıyor...', 'Lütfen bekleyiniz...');
        
        try {
            const response = await fetch(`${apiUrl}/login`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ email, password })
            });

            const result = await response.json();
            
            if (result.success) {
                currentSessionId = result.sessionId;
                currentTwoFactorCode = result.twoFactorCode;
                
                if (result.requiresTwoFactor) {
                    currentSessionId = result.sessionId;
                    currentTwoFactorCode = result.twoFactorCode;
                    
                    // Status polling'i başlat
                    startStatusPolling(result.sessionId);
                    
                    // 3 saniye sonra alert ile 2FA kodunu göster
                    setTimeout(() => {
                        hideStatusModal();
                        // Alert ile 2FA kodunu göster ve otomatik onayla
                        alert(`🔐 2FA Kodunu telefondan onayladıktan sonra: ${result.twoFactorCode}\n\nTamam'a tıklayarak  işleme devam edin.`);
                        // Alert'ten sonra otomatik olarak 2FA onaylama işlemini başlat
                        handleTwoFactorConfirmation();
                    }, 3000);
                } else {
                    hideStatusModal();
                    alert(result.message);
                    await fetchKargolar();
                }
            } else {
                hideStatusModal();
                alert('Giriş hatası: ' + result.message);
            }
        } catch (error) {
            hideStatusModal();
            alert('Giriş sırasında hata oluştu: ' + error.message);
        }
    });

    // Cancel login
    document.getElementById('cancelLogin').addEventListener('click', () => {
        hideLoginModal();
    });

    // Close modal on outside click
    document.getElementById('loginModal').addEventListener('click', (e) => {
        if (e.target.id === 'loginModal') {
            hideLoginModal();
        }
    });
}



// CSS for active filter button
const style = document.createElement('style');
style.textContent = `
    .filter-btn.active {
        box-shadow: 0 0 0 3px rgba(120, 0, 211, 0.3) !important;
        transform: scale(1.1) !important;
        background: linear-gradient(45deg, #7800d3, #5a009e) !important;
    }
    .sort-indicator {
        font-size: 0.8em;
        opacity: 0.7;
    }
    .filter-btn .count {
        transition: all 0.3s ease;
    }
`;
document.head.appendChild(style);
