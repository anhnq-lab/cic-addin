/* ========== TOOL DATA ========== */
const TOOLS = [
    {
        id: 'formwork',
        code: 'B3.2',
        name: 'Calculate Formwork',
        nameVi: 'Thống kê Ván khuôn',
        dept: 'kc',
        deptName: 'Kết cấu',
        icon: '🏗️',
        desc: 'Tự động tính toán diện tích ván khuôn cho các cấu kiện kết cấu (dầm, cột, tường, sàn, móng) trong model Revit.',
        tags: ['Revit API', 'Geometry', 'Schedule'],
        pain: [
            'Mở từng cấu kiện → đo thủ công từng mặt cần ván khuôn',
            'Tính tay: trừ phần giao nhau dầm-cột, dầm-sàn',
            'Nhập kết quả vào Excel → mất nhiều giờ, dễ sai sót',
            'Dự án lớn hàng trăm cấu kiện → nguy cơ bỏ sót cao'
        ],
        gain: [
            'Chọn cấu kiện hoặc chạy toàn bộ model → tool tự quét geometry',
            'Tự tính diện tích các mặt cần ván khuôn, tự trừ phần giao nhau',
            'Xuất kết quả ra bảng thống kê hoặc Excel → chỉ mất vài phút',
            'Chính xác 100% — không bỏ sót, không sai công thức'
        ],
        workflow: [
            { title: 'Chọn cấu kiện', desc: 'Chọn các cấu kiện KC (dầm, cột, tường, sàn, móng) trong model hoặc chạy toàn bộ' },
            { title: 'Phân tích Geometry', desc: 'Tool quét geometry 3D, xác định các mặt cần ván khuôn' },
            { title: 'Trừ giao nhau', desc: 'Tự động tính phần giao dầm-cột, dầm-sàn, cột-tường → trừ diện tích' },
            { title: 'Tổng hợp kết quả', desc: 'Xuất bảng thống kê theo loại cấu kiện hoặc export Excel' }
        ],
        params: [
            { icon: '📐', label: 'Cấu kiện', value: 'Dầm, Cột, Tường, Sàn, Móng' },
            { icon: '📊', label: 'Output', value: 'Schedule / Excel' },
            { icon: '🔗', label: 'Giao nhau', value: 'Tự trừ tự động' },
            { icon: '⚡', label: 'Tốc độ', value: '~2 phút / toàn bộ model' }
        ]
    },
    {
        id: 'plaster',
        code: 'B1.10',
        name: 'Plaster',
        nameVi: 'Lớp trát tường',
        dept: 'kt',
        deptName: 'Kiến trúc',
        icon: '🏛️',
        desc: 'Tự động tạo lớp trát cho tường, cột, dầm, sàn bên trong các Room được chọn. Hỗ trợ nhiều chiều dày khác nhau theo vị trí.',
        tags: ['Room Bound', 'Wall Type', 'Auto-Create'],
        pain: [
            'Tạo thủ công Wall Type mới cho lớp trát → rất nhiều Type',
            'Vẽ từng mảng tường trát trong Room → phải đo khoảng cách offset',
            'Cắt tay quanh cửa, cột, dầm → tốn thời gian',
            'Lớp trát khác nhau cho tường trong/ngoài, trần, sàn → dễ nhầm'
        ],
        gain: [
            'Chọn Room → tool tự quét boundary → tạo lớp trát bao quanh',
            'Tự nhận diện tường, cột, dầm, sàn trong Room',
            'Tự cắt quanh cửa, cấu kiện KC link',
            'Áp đúng chiều dày trát theo quy tắc (trong/ngoài/trần/sàn)'
        ],
        workflow: [
            { title: 'Chọn Room', desc: 'Chọn 1 hoặc nhiều Room cần trát trong model Revit' },
            { title: 'Cài đặt thông số', desc: 'Nhập chiều dày trát tường (trong/ngoài), trần, sàn' },
            { title: 'Quét Room Boundary', desc: 'Tool tự quét geometry Room, xác định mặt tường, trần, sàn' },
            { title: 'Tạo lớp trát', desc: 'Tự động tạo Wall/Floor element trát, cắt quanh cửa & cột' }
        ],
        params: [
            { icon: '📏', label: 'Chiều dày trát', value: '10-20mm (tùy chỉnh)' },
            { icon: '🏠', label: 'Phạm vi', value: 'Room-based' },
            { icon: '🚪', label: 'Cửa', value: 'Tự cắt quanh cửa' },
            { icon: '🔧', label: 'Vật liệu', value: 'Hỗ trợ nhiều Wall Type' }
        ]
    },
    {
        id: 'block-cad',
        code: 'B1.23',
        name: 'Block CAD → Equipment',
        nameVi: 'Đặt thiết bị theo Block CAD',
        dept: 'mep',
        deptName: 'Cơ điện',
        icon: '📦',
        desc: 'Đọc block CAD (kể cả block động / Dynamic Block) → tự động đặt Family Revit tương ứng tại đúng vị trí, đúng góc xoay.',
        tags: ['Dynamic Block', 'CAD Link', 'Family Placement'],
        pain: [
            'Nhìn bản vẽ CAD → xác định vị trí từng thiết bị từ block/symbol',
            'Đặt tay từng Family Revit vào đúng vị trí, đúng góc xoay',
            'Block động có nhiều trạng thái → khó xác định size/type',
            'Dự án lớn hàng trăm thiết bị → mất rất nhiều giờ, dễ sót'
        ],
        gain: [
            'Chọn layer/block name → tool tự nhận diện loại thiết bị',
            'Tự đặt Family Revit đúng vị trí, đúng góc xoay theo block CAD',
            'Đọc được thuộc tính Block động → chọn đúng Type/Size Family',
            'Xử lý hàng trăm thiết bị chỉ trong vài phút'
        ],
        workflow: [
            { title: 'Link file CAD', desc: 'Link file .dwg có block thiết bị vào model Revit' },
            { title: 'Mapping Block → Family', desc: 'Thiết lập bảng mapping: Block CAD → Family Revit + Type' },
            { title: 'Chọn Layer / Block', desc: 'Chọn layer hoặc block name cần xử lý trong CAD' },
            { title: 'Đặt thiết bị', desc: 'Tool tự đặt Family tại vị trí block, đúng góc xoay, đúng cao độ' }
        ],
        params: [
            { icon: '📦', label: 'Block Type', value: 'Static + Dynamic Block' },
            { icon: '🔄', label: 'Góc xoay', value: 'Tự lấy từ Block CAD' },
            { icon: '📋', label: 'Mapping', value: 'Block Name → Family' },
            { icon: '📏', label: 'Cao độ', value: 'Theo loại thiết bị' }
        ]
    },
    {
        id: 'duct-cad',
        code: 'B1.24',
        name: 'Duct from CAD',
        nameVi: 'Ống gió từ line CAD',
        dept: 'mep',
        deptName: 'Cơ điện',
        icon: '💨',
        desc: 'Dựng ống gió (Duct) tự động trong Revit từ line trong bản vẽ CAD link. Mapping Line Type/Layer → Duct Type + Size (W×H hoặc Ø).',
        tags: ['HVAC', 'CAD to Duct', 'Auto-Fitting'],
        pain: [
            'Nhìn bản vẽ CAD → xác định tuyến ống gió, kích thước W×H',
            'Vẽ tay từng đoạn duct trong Revit, chọn đúng Duct Type + Size',
            'Nhập cao độ thủ công cho từng đoạn',
            'Nối fitting (co, tê, chuyển tiếp) bằng tay → rất chậm'
        ],
        gain: [
            'Chọn layer/line type → tool tự nhận diện tuyến ống gió',
            'Tự dựng duct theo đúng đường đi trong CAD',
            'Áp đúng Duct Type, Size (W×H hoặc Ø), System Type, cao độ',
            'Tùy chọn tự nối fitting → tiết kiệm hàng giờ cho mỗi tầng'
        ],
        workflow: [
            { title: 'Link file CAD', desc: 'Link file .dwg có hệ thống ống gió vào model Revit' },
            { title: 'Mapping Line → Duct', desc: 'Thiết lập bảng: Line Type/Layer → Duct Type + W×H + System' },
            { title: 'Cài đặt thông số', desc: 'Nhập cao độ, mốc tính (BOD/CL/TOD), System Type' },
            { title: 'Tạo Duct', desc: 'Tool tự dựng duct theo tuyến CAD, tùy chọn nối fitting' }
        ],
        params: [
            { icon: '📐', label: 'Hình dạng', value: 'Rect / Round / Oval' },
            { icon: '🔗', label: 'Fitting', value: 'Tự nối (tùy chọn)' },
            { icon: '📏', label: 'Cao độ', value: 'BOD / CL / TOD' },
            { icon: '🌬️', label: 'System', value: 'SA / RA / EA / FA' }
        ]
    },
    {
        id: 'pipe-slope',
        code: 'B1.28',
        name: 'Pipe Slope Adjustment',
        nameVi: 'Thay đổi độ dốc ống',
        dept: 'mep',
        deptName: 'Cơ điện',
        icon: '📐',
        desc: 'Nhập giá trị độ dốc (VD: 1%) → ống thoát nước và các fitting (co, tê) tự động xoay theo góc tương ứng.',
        tags: ['Sanitary', 'Slope', 'Fitting Rotate'],
        pain: [
            'Chọn từng đoạn ống → nhập thủ công thông số Slope',
            'Fitting (co, tê) không tự xoay → phải chỉnh tay từng cái',
            'Thay đổi dốc 1 đoạn → phải cập nhật lại cao độ các đoạn kế tiếp',
            'Dễ bị xung đột (clash) vì fitting không khớp góc'
        ],
        gain: [
            'Chọn hệ thống ống → nhập 1 giá trị dốc (VD: 1%)',
            'Tool tự áp slope cho tất cả đoạn ống được chọn',
            'Fitting tự xoay theo đúng góc tương ứng',
            'Tự cập nhật cao độ liên hoàn → không clash'
        ],
        workflow: [
            { title: 'Chọn hệ thống ống', desc: 'Chọn ống thoát nước / nước mưa cần thay đổi độ dốc' },
            { title: 'Nhập giá trị dốc', desc: 'Nhập % hoặc tỉ lệ (VD: 1%, 2%, 1:100)' },
            { title: 'Chọn điểm cố định', desc: 'Chọn đầu nào giữ nguyên (upstream/downstream)' },
            { title: 'Áp dốc', desc: 'Tool tự tính góc, áp slope, xoay fitting, cập nhật cao độ' }
        ],
        params: [
            { icon: '📐', label: 'Độ dốc', value: '% hoặc tỉ lệ' },
            { icon: '🔧', label: 'Fitting', value: 'Tự xoay theo góc' },
            { icon: '📍', label: 'Điểm cố định', value: 'Upstream/Downstream' },
            { icon: '🔄', label: 'Cao độ', value: 'Tự cập nhật liên hoàn' }
        ]
    }
];

const DEPT_META = {
    kc: { name: 'KC — Kết cấu', fullName: 'Kết cấu (Structural)', icon: '🏗️', color: 'kc' },
    kt: { name: 'KT — Kiến trúc', fullName: 'Kiến trúc (Architecture)', icon: '🏛️', color: 'kt' },
    mep: { name: 'MEP — Cơ điện', fullName: 'Cơ điện (MEP)', icon: '⚡', color: 'mep' }
};

/* ========== RENDER FUNCTIONS ========== */
function renderToolCard(tool) {
    return `
    <div class="tool-card" data-dept="${tool.dept}" data-id="${tool.id}" onclick="openModal('${tool.id}')">
      <div class="tool-card-header">
        <div class="tool-card-icon">${tool.icon}</div>
        <div>
          <div class="tool-card-title">${tool.nameVi}</div>
          <div class="tool-card-code">${tool.code} — ${tool.name}</div>
        </div>
      </div>
      <div class="tool-card-desc">${tool.desc}</div>
      <div class="tool-card-footer">
        <div class="tool-card-tags">
          ${tool.tags.map(t => `<span class="tool-tag">${t}</span>`).join('')}
        </div>
        <div class="tool-card-arrow">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M5 12h14M12 5l7 7-7 7"/></svg>
        </div>
      </div>
    </div>`;
}

function renderDeptSection(dept, tools) {
    const meta = DEPT_META[dept];
    return `
    <section class="dept-section" id="section-${dept}">
      <div class="dept-header">
        <span class="dept-header-icon">${meta.icon}</span>
        <h2>${meta.name}</h2>
        <span class="dept-tag dept-tag--${dept}">${meta.fullName}</span>
        <div class="dept-line dept-line--${dept}"></div>
      </div>
      <div class="tool-grid">
        ${tools.map(renderToolCard).join('')}
      </div>
    </section>`;
}

function renderDashboard() {
    const container = document.getElementById('toolCardsContainer');
    const grouped = {};
    for (const tool of TOOLS) {
        if (!grouped[tool.dept]) grouped[tool.dept] = [];
        grouped[tool.dept].push(tool);
    }
    const order = ['kc', 'kt', 'mep'];
    container.innerHTML = order
        .filter(d => grouped[d])
        .map(d => renderDeptSection(d, grouped[d]))
        .join('');
}

/* ========== MODAL ========== */
function renderModal(tool) {
    const meta = DEPT_META[tool.dept];
    return `
    <div class="modal-hero">
      <div class="modal-hero-icon" style="background:var(--${tool.dept}-bg)">${tool.icon}</div>
      <div>
        <div class="modal-hero-title">${tool.nameVi}</div>
        <span class="modal-hero-tag dept-tag--${tool.dept}" style="background:var(--${tool.dept}-bg);color:var(--${tool.dept});border:1px solid rgba(0,0,0,.1)">
          ${tool.code} — ${meta.fullName}
        </span>
      </div>
    </div>

    <div class="modal-desc">${tool.desc}</div>

    <div class="pain-gain">
      <div class="pain-box">
        <h4>🔴 Hiện tại (thủ công)</h4>
        <ul>${tool.pain.map(p => `<li>• ${p}</li>`).join('')}</ul>
      </div>
      <div class="gain-box">
        <h4>🟢 Sau khi có tool</h4>
        <ul>${tool.gain.map(g => `<li>• ${g}</li>`).join('')}</ul>
      </div>
    </div>

    <div class="workflow-section">
      <div class="workflow-title">⚙️ Quy trình hoạt động</div>
      <div class="workflow-steps">
        ${tool.workflow.map((step, i) => `
          <div class="workflow-step">
            <div class="step-num" style="background:var(--${tool.dept}-bg);color:var(--${tool.dept})">${i + 1}</div>
            <div class="step-text"><strong>${step.title}</strong><br><span>${step.desc}</span></div>
          </div>
        `).join('')}
      </div>
    </div>

    <div class="params-section">
      <div class="workflow-title">📋 Thông số chính</div>
      <div class="params-grid">
        ${tool.params.map(p => `
          <div class="param-item">
            <span class="param-icon">${p.icon}</span>
            <div>
              <span class="param-label">${p.label}</span>
              <span class="param-value">${p.value}</span>
            </div>
          </div>
        `).join('')}
      </div>
    </div>`;
}

function openModal(toolId) {
    const tool = TOOLS.find(t => t.id === toolId);
    if (!tool) return;
    document.getElementById('modalContent').innerHTML = renderModal(tool);
    document.getElementById('modalOverlay').classList.add('active');
    document.body.style.overflow = 'hidden';
}

function closeModal() {
    document.getElementById('modalOverlay').classList.remove('active');
    document.body.style.overflow = '';
}

/* ========== SIDEBAR NAV ========== */
function setActiveNav(section) {
    document.querySelectorAll('.nav-item').forEach(el => el.classList.remove('active'));
    const target = document.querySelector(`.nav-item[data-section="${section}"]`);
    if (target) target.classList.add('active');

    const title = document.getElementById('pageTitle');
    const subtitle = document.getElementById('pageSubtitle');

    if (section === 'dashboard') {
        title.textContent = 'Dashboard';
        subtitle.textContent = '5 tools ưu tiên phát triển — nhóm theo bộ môn';
        document.querySelectorAll('.dept-section').forEach(s => s.style.display = '');
        document.getElementById('statsRow').style.display = '';
    } else {
        const meta = DEPT_META[section];
        title.textContent = meta.name;
        subtitle.textContent = meta.fullName;
        document.querySelectorAll('.dept-section').forEach(s => {
            s.style.display = s.id === `section-${section}` ? '' : 'none';
        });
        document.getElementById('statsRow').style.display = 'none';
    }
}

/* ========== INIT ========== */
document.addEventListener('DOMContentLoaded', () => {
    renderDashboard();

    // Sidebar nav clicks
    document.querySelectorAll('.nav-item').forEach(el => {
        el.addEventListener('click', e => {
            e.preventDefault();
            const section = el.dataset.section;
            setActiveNav(section);
            // Close sidebar on mobile
            document.getElementById('sidebar').classList.remove('open');
        });
    });

    // Modal close
    document.getElementById('modalClose').addEventListener('click', closeModal);
    document.getElementById('modalOverlay').addEventListener('click', e => {
        if (e.target === e.currentTarget) closeModal();
    });
    document.addEventListener('keydown', e => {
        if (e.key === 'Escape') closeModal();
    });

    // Mobile menu
    document.getElementById('menuToggle').addEventListener('click', () => {
        document.getElementById('sidebar').classList.toggle('open');
    });
});
