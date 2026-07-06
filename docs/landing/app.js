(function () {
    'use strict';

    var grid = document.getElementById('screenshotGrid');
    var lightbox = document.getElementById('lightbox');
    var lightboxImg = document.getElementById('lightboxImg');

    if (!grid || !lightbox || !lightboxImg) return;

    grid.addEventListener('click', function (e) {
        if (e.target.tagName !== 'IMG') return;
        var full = e.target.getAttribute('data-full');
        if (!full) return;
        lightboxImg.src = full;
        lightboxImg.alt = e.target.alt;
        lightbox.classList.add('active');
    });

    lightbox.addEventListener('click', function () {
        lightbox.classList.remove('active');
    });

    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') {
            lightbox.classList.remove('active');
        }
    });
})();
