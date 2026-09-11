/* HVACIDA UI 原型 — 交互逻辑
 * 说明:本文件内的计算仅为"原型演示",公式链与 HVACIDA.Core 的实现保持一致
 * (北京算例可作为对照:总制冷 381.6 kW)。正式数值以插件为准。
 */
(function () {
  'use strict';

  // =========================================================================
  // 0. 工具
  // =========================================================================
  var $ = function (sel, root) { return (root || document).querySelector(sel); };
  var $$ = function (sel, root) { return Array.prototype.slice.call((root || document).querySelectorAll(sel)); };

  function fmt(v, dec, thousand) {
    if (!isFinite(v)) return '—';
    var s = Number(v).toFixed(dec == null ? 2 : dec);
    if (thousand) {
      var p = s.split('.');
      p[0] = p[0].replace(/\B(?=(\d{3})+(?!\d))/g, ',');
      s = p.join('.');
    }
    return s;
  }
  function setStatus(el, text, isErr) {
    if (!el) return;
    el.textContent = text || '';
    el.className = 'status' + (isErr ? ' err' : '');
  }
  function appStatus(text) { $('#app-status').textContent = text; }
  function num(v) { var n = parseFloat(v); return isFinite(n) ? n : 0; }

  // =========================================================================
  // 1. 窗口管理(模态/层叠/拖动)
  // =========================================================================
  var zTop = 100;
  function openWin(id, onOpen) {
    var w = document.getElementById(id);
    if (!w) return null;
    w.classList.add('open');
    w.style.zIndex = ++zTop;
    if (onOpen) onOpen(w);
    return w;
  }
  function closeWin(w) { w.classList.remove('open'); }
  function winOf(el) { return el.closest ? el.closest('.win') : null; }

  document.addEventListener('click', function (e) {
    var t = e.target;
    if (t.hasAttribute && t.hasAttribute('data-close')) {
      var w = winOf(t);
      if (w) { closeWin(w); appStatus('已关闭:' + ($('.titlebar', w).textContent || '').replace('✕', '').trim()); }
      return;
    }
    if (t.hasAttribute && t.hasAttribute('data-open')) {
      var id = t.getAttribute('data-open');
      openWin(id);
      appStatus('已打开窗口:' + id.replace('win-', ''));
    }
  });

  // 拖动
  document.addEventListener('mousedown', function (e) {
    var bar = e.target.closest ? e.target.closest('.titlebar') : null;
    if (!bar || e.target.classList.contains('close')) return;
    var w = winOf(bar);
    if (!w) return;
    w.style.zIndex = ++zTop;
    var ox = e.clientX - w.offsetLeft, oy = e.clientY - w.offsetTop;
    function move(ev) {
      w.style.left = Math.max(0, ev.clientX - ox) + 'px';
      w.style.top = Math.max(0, ev.clientY - oy) + 'px';
    }
    function up() {
      document.removeEventListener('mousemove', move);
      document.removeEventListener('mouseup', up);
    }
    document.addEventListener('mousemove', move);
    document.addEventListener('mouseup', up);
  });

  // 通用确认框
  var confirmCb = null;
  function askConfirm(msg, cb) {
    $('#confirm-msg').textContent = msg;
    confirmCb = cb;
    openWin('win-confirm');
  }
  $('#confirm-yes').addEventListener('click', function () {
    closeWin($('#win-confirm'));
    if (confirmCb) { var cb = confirmCb; confirmCb = null; cb(); }
  });

  // =========================================================================
  // 2. 视图切换(界面原型 / 交互流程 / 交互逻辑表)
  // =========================================================================
  function setView(view) {
    $$('.tab-btn').forEach(function (b) { b.classList.toggle('active', b.getAttribute('data-view') === view); });
    var canvas = $('#canvas'), status = $('#app-status');
    var pageFlow = $('#page-flow'), pageLogic = $('#page-logic');
    pageFlow.classList.toggle('open', view === 'flow');
    pageLogic.classList.toggle('open', view === 'logic');
    var isUi = view === 'ui';
    canvas.style.display = isUi ? '' : 'none';
    status.style.display = isUi ? '' : 'none';
    if (!isUi) { $$('.win.open').forEach(function (w) { w.classList.remove('open'); }); }
    document.querySelector('.ribbon').style.display = isUi ? '' : 'none';
    if (location.hash !== '#' + view) { try { location.hash = view; } catch (e) { /* file:// 下允许失败 */ } }
  }
  $$('.tab-btn').forEach(function (b) {
    b.addEventListener('click', function () { setView(b.getAttribute('data-view')); });
  });
  window.addEventListener('hashchange', function () {
    var v = (location.hash || '#ui').slice(1);
    if (['ui', 'flow', 'logic'].indexOf(v) >= 0) setView(v);
  });

  // =========================================================================
  // 3. 字段规格(标签/键/单位/默认值)
  // =========================================================================
  // 3.1 项目信息
  var PROJ = {
    basic: [
      { k: 'projectName', label: '工程名称', type: 'text', def: '北京地铁 XX 站' },
      { k: 'designStage', label: '设计阶段', type: 'select', options: ['初步设计', '施工图设计'] },
      { k: 'province', label: '项目地点-省', type: 'text', def: '北京市' },
      { k: 'city', label: '项目地点-市', type: 'text', def: '北京市' },
      { k: 'district', label: '项目地点-区/县', type: 'text', def: '海淀区' },
      { k: 'stationName', label: '车站名称', type: 'text', def: 'XX 站' },
      { k: 'remark', label: '备注', type: 'text', def: '' }
    ],
    outLarge: [
      { k: 'summerDry', label: '夏季空调干球 ℃', def: 31 },
      { k: 'summerWet', label: '夏季空调湿球 ℃', def: 25 },
      { k: 'summerVent', label: '夏季通风 ℃', def: 26.4 },
      { k: 'winterVent', label: '冬季通风 ℃', def: -3.6 },
      { k: 'winterAC', label: '冬季空调 ℃', def: -7.6 },
      { k: 'atm', label: '大气压力 kPa', def: 102.17 },
      { k: 'outRH', label: '室外相对湿度 %', def: 60 }
    ],
    outSmall: [
      { k: 'sSummerDry', label: '夏季空调干球 ℃', def: 31 },
      { k: 'sSummerWet', label: '夏季空调湿球 ℃', def: 25 },
      { k: 'sSummerVent', label: '夏季通风 ℃', def: 26.4 }
    ],
    indoor: [
      { k: 'hallDry', label: '站厅干球 ℃', def: 29 },
      { k: 'hallRH', label: '站厅相对湿度 %', def: 60 },
      { k: 'platDry', label: '站台干球 ℃', def: 27 },
      { k: 'platRH', label: '站台相对湿度 %', def: 60 },
      { k: 'mgmtDry', label: '管理用房干球 ℃', def: 26 },
      { k: 'weakDry', label: '弱电用房干球 ℃', def: 26 },
      { k: 'strongDry', label: '强电用房干球 ℃', def: 28 },
      { k: 'indoorWet', label: '室内计算湿球 ℃', def: 20 }
    ]
  };

  // 3.2 大系统(键名与 HVACIDA.Core.Models.LargeSystemInput 一致)
  var LARGE_SPEC = [
    { title: '一、空气计算参数及标准', fields: [
      { k: 'tw', label: '夏季空调室外湿球温度 ℃', def: 25 },
      { k: 'tHall', label: '站厅空调计算干球温度 ℃', def: 29 },
      { k: 'tPlat', label: '站台空调计算干球温度 ℃', def: 27 },
      { k: 'dT', label: '站厅公共区风温差 ℃', def: 10 },
      { k: 'ductRise', label: '管道温升 ℃', def: 1.5 },
      { k: 'dewRH', label: '露点相对湿度 %', def: 95 },
      { k: 'wallMoist', label: '壁面产湿量 g/(m²·h)', def: 1 },
      { k: 'freshPerPerson', label: '空调季新风指标 m³/(h·人)', def: 20 }
    ] },
    { title: '二、车站几何与出入口', fields: [
      { k: 'hallArea', label: '站厅公共区面积 m²', def: 2000, pick: true },
      { k: 'platArea', label: '站台公共区面积 m²', def: 1620, pick: true },
      { k: 'hallHeight', label: '站厅公共区层高 m', def: 4.9, pick: true },
      { k: 'hallLength', label: '站厅公共区长度 m', def: 101, pick: true },
      { k: 'entAW', label: '出入口A 宽 m', def: 5.3 },
      { k: 'entAH', label: '出入口A 高 m', def: 4 },
      { k: 'entBW', label: '出入口B 宽 m', def: 5.3 },
      { k: 'entBH', label: '出入口B 高 m', def: 4 },
      { k: 'entCW', label: '出入口C 宽 m', def: 6 },
      { k: 'entCH', label: '出入口C 高 m', def: 4 },
      { k: 'entDW', label: '出入口D 宽 m', def: 4 },
      { k: 'entDH', label: '出入口D 高 m', def: 4 },
      { k: 'entIndex', label: '出入口负荷指标 W', def: 200 }
    ] },
    { title: '三、高峰客流资料(必填)', required: true, fields: [
      { k: 'upBoard', label: '上行线上客量 人次/h', def: 701 },
      { k: 'upAlight', label: '上行线下客量 人次/h', def: 1633 },
      { k: 'downBoard', label: '下行线上客量 人次/h', def: 1206 },
      { k: 'downAlight', label: '下行线下客量 人次/h', def: 574 },
      { k: 'xferBoard', label: '换乘上客量 人次/h', def: 0 },
      { k: 'xferAlight', label: '换乘下客量 人次/h', def: 0 },
      { k: 'hallBoardStay', label: '站厅 上车停站 min', def: 2 },
      { k: 'hallAlightStay', label: '站厅 下车停站 min', def: 1.5 },
      { k: 'hallXBoardStay', label: '站厅 换乘上车 min', def: 2 },
      { k: 'hallXAlightStay', label: '站厅 换乘下车 min', def: 1.5 },
      { k: 'platBoardStay', label: '站台 上车停站 min', def: 2 },
      { k: 'platAlightStay', label: '站台 下车停站 min', def: 1.5 },
      { k: 'platXBoardStay', label: '站台 换乘上车 min', def: 2 },
      { k: 'platXAlightStay', label: '站台 换乘下车 min', def: 1.5 },
      { k: 'cluster', label: '集群系数', def: 0.89 },
      { k: 'superPeak', label: '超高峰小时系数', def: 1 }
    ] },
    { title: '四、人员散热、散湿量标准', fields: [
      { k: 'hallSens', label: '站厅 显热 W/人', def: 40 },
      { k: 'hallLat', label: '站厅 潜热 W/人', def: 142 },
      { k: 'hallMoist', label: '站厅 散湿量 g/h', def: 212 },
      { k: 'platSens', label: '站台 显热 W/人', def: 51 },
      { k: 'platLat', label: '站台 潜热 W/人', def: 130 },
      { k: 'platMoist', label: '站台 散湿量 g/h', def: 194 }
    ] },
    { title: '五、照明 / 广告 / 设备发热量', fields: [
      { k: 'hallLight', label: '站厅照明指标 W/m²', def: 8 },
      { k: 'platLight', label: '站台照明指标 W/m²', def: 8 },
      { k: 'hallAdvert', label: '站厅广告牌 kW', def: 60 },
      { k: 'platAdvert', label: '站台广告牌 kW', def: 10 },
      { k: 'escKw', label: '扶梯指标 kW/台', def: 1.5 },
      { k: 'escCount', label: '扶梯数量 台', def: 4 },
      { k: 'elevKw', label: '直梯指标 kW/台', def: 1.5 },
      { k: 'elevCount', label: '直梯数量 台', def: 1 },
      { k: 'afcKw', label: 'AFC 指标 kW/台', def: 18 },
      { k: 'afcCount', label: 'AFC 数量 台', def: 1 }
    ] },
    { title: '六、屏蔽门传热 / 漏风 / 发热', fields: [
      { k: 'psdK', label: '传热系数 W/(m²·℃)', def: 3.2 },
      { k: 'psdH', label: '屏蔽门高 m', def: 3 },
      { k: 'psdL', label: '屏蔽门长 m', def: 292 },
      { k: 'psdDT', label: '内外温差 ℃', def: 8 },
      { k: 'psdSafe', label: '传热安全系数', def: 1.5 },
      { k: 'hallPsdTransfer', label: '站厅屏蔽门传热 kW', def: 0 },
      { k: 'hallPsdLeak', label: '站厅屏蔽门漏风 kW', def: 30 },
      { k: 'hallPsdHeat', label: '站厅屏蔽门发热 kW', def: 0 },
      { k: 'platPsdLeak', label: '站台屏蔽门漏风 kW', def: 45 },
      { k: 'platPsdHeat', label: '屏蔽门系统发热 kW', def: 4 }
    ] },
    { title: '七、其他湿负荷 / 补充发热', fields: [
      { k: 'hallOtherMoist', label: '站厅其他湿负荷', def: 0 },
      { k: 'platOtherMoist', label: '站台其他湿负荷', def: 0 },
      { k: 'platStructMoist', label: '站台结构散湿(0=不计)', def: 0 },
      { k: 'hallExtra', label: '站厅补充发热 kW', def: 0 },
      { k: 'platExtra', label: '站台补充发热 kW', def: 0 }
    ] }
  ];

  var SAMPLE = {
    tw: 25, tHall: 29, tPlat: 27, dT: 10, ductRise: 1.5, dewRH: 95, freshPerPerson: 20,
    hallArea: 2000, platArea: 1620, hallHeight: 4.9, hallLength: 101,
    entAW: 5.3, entAH: 4, entBW: 5.3, entBH: 4, entCW: 6, entCH: 4, entDW: 4, entDH: 4, entIndex: 200,
    upBoard: 701, upAlight: 1633, downBoard: 1206, downAlight: 574, xferBoard: 0, xferAlight: 0,
    cluster: 0.89, superPeak: 1,
    hallLight: 8, platLight: 8, hallAdvert: 60, platAdvert: 10,
    escKw: 1.5, escCount: 4, elevKw: 1.5, elevCount: 1, afcKw: 18, afcCount: 1,
    hallPsdTransfer: 0, hallPsdLeak: 30, hallPsdHeat: 0, platPsdLeak: 45, platPsdHeat: 4
  };

  function specDefaults(spec) {
    var o = {};
    spec.forEach(function (sec) { sec.fields.forEach(function (f) { o[f.k] = f.def; }); });
    return o;
  }
  var DEFAULT_L = specDefaults(LARGE_SPEC);
  var L = JSON.parse(JSON.stringify(DEFAULT_L));
  var largeDirty = false;

  // =========================================================================
  // 4. 大系统:渲染 / 计算 / 结果
  // =========================================================================
  function renderLargeInputs() {
    var host = $('#large-inputs');
    var html = '';
    LARGE_SPEC.forEach(function (sec, si) {
      html += '<div class="group" data-sec="' + si + '"><div class="gtitle">' + sec.title +
        (sec.required ? '<span class="warn-chip hidden" data-req="1">必填</span>' : '') + '</div><div class="grid2">';
      sec.fields.forEach(function (f) {
        html += '<div class="row"><label>' + f.label + '</label>' +
          '<input type="number" data-k="' + f.k + '" value="' + L[f.k] + '"' +
          (f.pick ? ' class="num-lg"' : '') + '></div>';
      });
      html += '</div></div>';
    });
    host.innerHTML = html;
    $$('input[data-k]', host).forEach(function (inp) {
      inp.addEventListener('input', function () {
        L[inp.getAttribute('data-k')] = num(inp.value);
        inp.classList.remove('invalid');
        largeDirty = true;
      });
    });
  }

  function enthalpy(t, dGkg) { return 1.01 * t + (2500 + 1.84 * t) * dGkg / 1000 + 0.4; }
  function satMoisture(t) {
    return -0.0000000004171 * Math.pow(t, 7) + 0.00000004843 * Math.pow(t, 6)
      - 0.000002133 * Math.pow(t, 5) + 0.00005009 * Math.pow(t, 4)
      - 0.0004032 * Math.pow(t, 3) + 0.01264 * Math.pow(t, 2) + 0.265 * t + 3.787;
  }
  function safe(v) { return (!isFinite(v) || v < 0) ? 0 : v; }
  function flowByEnthalpy(qKw, hIn, hSup) {
    var diff = hIn - hSup;
    if (diff < 1.0) return 0;             // 退化保护(与 Core 一致)
    return safe(qKw / 1.15 / diff * 3600);
  }

  function calcLarge() {
    var r = {};
    // 高峰客流(个/min)
    var c35 = (L.upBoard + L.downBoard) / 60 * L.hallBoardStay + L.xferBoard / 60 * L.hallXBoardStay;
    var c36 = (L.upAlight + L.downAlight) / 60 * L.hallAlightStay + L.xferAlight / 60 * L.hallXAlightStay;
    var f35 = (L.upBoard + L.downBoard) / 60 * L.platBoardStay + L.xferBoard / 60 * L.platXBoardStay;
    var f36 = (L.upAlight + L.downAlight) / 60 * L.platAlightStay + L.xferAlight / 60 * L.platXAlightStay;
    r.C39 = (c35 + c36) * L.cluster * L.superPeak;
    r.C40 = (f35 + f36) * L.cluster * L.superPeak;

    // 冷负荷 kW
    r.D95 = L.hallSens * r.C39 / 1000;
    r.D96 = L.hallLat * r.C39 / 1000;
    r.D97 = L.hallLight * L.hallArea / 1000;
    r.D98 = L.hallAdvert;
    r.B64 = L.escKw * L.escCount;
    r.D99 = r.B64 / 2;
    r.D64 = L.elevKw * L.elevCount;
    r.D100 = r.D64 / 2;
    r.E64 = L.afcKw * L.afcCount;
    r.D101 = r.E64;
    r.D102 = L.entIndex * (L.entAW * L.entAH + L.entBW * L.entBH + L.entCW * L.entCH + L.entDW * L.entDH) / 1000;
    r.D107 = r.D95 + r.D96 + r.D97 + r.D98 + r.D99 + r.D100 + r.D101 + r.D102 +
      L.hallPsdTransfer + L.hallPsdLeak + L.hallPsdHeat + L.hallExtra;

    r.E95 = L.platSens * r.C40 / 1000;
    r.E96 = L.platLat * r.C40 / 1000;
    r.E97 = L.platLight * L.platArea / 1000;
    r.E98 = L.platAdvert;
    r.E99 = r.B64 / 2;
    r.E100 = r.D64 / 2;
    r.F71 = L.psdK * L.psdH * L.psdL * L.psdDT * L.psdSafe / 1000;
    r.E107 = r.E95 + r.E96 + r.E97 + r.E98 + r.E99 + r.E100 + r.F71 + L.platPsdLeak + L.platPsdHeat + L.platExtra;

    // 湿负荷 g/s
    r.D108 = r.C39 * L.hallMoist / 1000;
    r.B91 = L.hallHeight * 2 * L.hallLength + L.hallArea;
    r.D109 = L.wallMoist * r.B91 / 1000;
    r.D112 = (r.D108 + r.D109 + L.hallOtherMoist) * 1000 / 3600;
    r.E108 = r.C40 * L.platMoist / 1000;
    r.E112 = (r.E108 + L.platStructMoist + L.platOtherMoist) * 1000 / 3600;

    // 热湿比与焓湿
    r.D113 = r.D112 > 1e-9 ? r.D107 / r.D112 * 1000 : 0;
    r.E113 = r.E112 > 1e-9 ? r.E107 / r.E112 * 1000 : 0;
    r.A118 = L.tHall - L.dT;
    r.B118 = r.A118 - L.ductRise;
    r.D118 = satMoisture(r.B118);
    r.E118 = L.dewRH / 100 * r.D118;
    r.A121 = enthalpy(r.A118, r.E118);
    var denH = (2500 + 1.84 * L.tHall) - r.D113;
    r.B121 = Math.abs(denH) > 1e-9 ? (r.A121 * 1000 - r.E118 * r.D113 - 1.01 * L.tHall * 1000) / denH : r.E118;
    r.C121 = enthalpy(L.tHall, r.B121);
    var denP = (2500 + 1.84 * L.tPlat) - r.E113;
    r.D121 = Math.abs(denP) > 1e-9 ? (r.A121 * 1000 - r.E118 * r.E113 - 1.01 * L.tPlat * 1000) / denP : r.E118;
    r.E121 = enthalpy(L.tPlat, r.D121);

    // 风量
    r.A125 = flowByEnthalpy(r.D107, r.C121, r.A121);
    r.B125 = flowByEnthalpy(r.E107, r.E121, r.A121);
    r.C125 = r.A125 + r.B125;

    // 新风 / 回风
    r.A132 = r.C39 + r.C40;
    r.A136 = Math.max(r.A132 * L.freshPerPerson, r.C125 * 0.1);
    r.B136 = r.C125 > 1e-9 ? r.A136 / r.C125 : 0;
    r.C136 = r.A125 * (1 - r.B136);
    r.D136 = r.B125 * (1 - r.B136);
    r.E136 = r.C136 + r.D136;

    // 制冷量
    r.C145 = r.E136 > 1e-9 ? (r.C121 * r.C136 + r.E121 * r.D136) / r.E136 : 0;
    r.D149 = satMoisture(L.tw);
    r.C149 = enthalpy(L.tw, r.D149);
    r.C146 = r.C125 > 1e-9 ? (r.C145 * r.E136 + r.C149 * r.A136) / r.C125 : 0;
    r.C143 = enthalpy(r.B118, r.E118);
    r.E159 = safe(r.C125 * 1.15 * (r.C146 - r.C143) / 3600);

    // 排烟(计算风量 ×60)与选型
    r.C171 = L.hallArea * 60;
    r.D171 = L.platArea * 60;
    r.A165 = r.C125 / 2;
    r.B165 = r.E159 / 2;
    r.C178 = r.E136 / 2;
    r.E178 = Math.max(r.C171, r.D171) / 2;
    return r;
  }

  function kv(k, v, unit, code, strong) {
    return '<div class="kv' + (strong ? ' strong' : '') + '"><span class="k">' + k + '</span>' +
      '<span class="v">' + v + '</span><span class="u">' + (unit || '') + '</span>' +
      '<span class="code">' + (code || '') + '</span></div>';
  }

  function renderLargeResults(r, invalid) {
    var host = $('#large-results');
    if (!r) {
      host.innerHTML = '<div class="card"><div class="chead">计算结果</div><div class="cbody">' +
        '<div style="color:#5A5A5A;">点击底栏【计 算】生成结果;<br>结果将按四组卡片展示(客流 / 冷负荷 / 风量与制冷 / 设备选型)。</div></div></div>';
      return;
    }
    var html = '';
    html += '<div class="card"><div class="chead">高峰客流</div><div class="cbody">' +
      kv('站厅公共区', fmt(r.C39, 1), '个/min', 'C39') +
      kv('站台公共区', fmt(r.C40, 1), '个/min', 'C40') +
      (invalid ? '<div class="warn-chip">缺少必填参数:高峰客流全部为 0</div>' : '') + '</div></div>';

    html += '<div class="card"><div class="chead">冷负荷(kW)</div><div class="cbody">' +
      kv('站厅合计', fmt(r.D107, 2), 'kW', 'D107', true) +
      kv('站台合计', fmt(r.E107, 2), 'kW', 'E107', true) +
      kv('人员显热(站厅/站台)', fmt(r.D95, 2) + ' / ' + fmt(r.E95, 2), 'kW', 'D95/E95') +
      kv('人员潜热(站厅/站台)', fmt(r.D96, 2) + ' / ' + fmt(r.E96, 2), 'kW', 'D96/E96') +
      '</div></div>';

    html += '<div class="card"><div class="chead">风量与制冷</div><div class="cbody">' +
      kv('站厅送风量', fmt(r.A125, 0, true), 'm³/h', 'A125') +
      kv('站台送风量', fmt(r.B125, 0, true), 'm³/h', 'B125') +
      kv('总送风量', fmt(r.C125, 0, true), 'm³/h', 'C125', true) +
      kv('实际新风量', fmt(r.A136, 0, true), 'm³/h', 'A136') +
      kv('新风比', fmt(r.B136 * 100, 1), '%', 'B136') +
      kv('总回风量', fmt(r.E136, 0, true), 'm³/h', 'E136') +
      kv('露点含湿量', fmt(r.E118, 2), 'g/kg', 'E118') +
      kv('送风点焓', fmt(r.A121, 2), 'kJ/kg', 'A121') +
      kv('总制冷量', fmt(r.E159, 2), 'kW', 'E159', true) +
      '</div></div>';

    html += '<div class="card"><div class="chead">设备选型(单台 / 每端,共 2 台)</div><div class="cbody">' +
      kv('组合式空调机组 送风量', fmt(r.A165, 0, true), 'm³/h', 'A165') +
      kv('组合式空调机组 制冷量', fmt(r.B165, 2), 'kW', 'B165') +
      kv('回排风机 回风量', fmt(r.C178, 0, true), 'm³/h', 'C178') +
      kv('排烟风机 风量', fmt(r.E178, 0, true), 'm³/h', 'E178') +
      '</div></div>';

    html += '<div class="card"><div class="chead">提示</div><div class="cbody" style="color:#5A5A5A;">' +
      '排烟为<b>计算风量</b>(面积×60);选型风量 = 计算×1.2(防烟分区口径)。<br>' +
      '站厅/站台送风温度一致(本窗 A118=' + fmt(r.A118, 1) + ' ℃)。</div></div>';
    host.innerHTML = html;
  }

  var lastLarge = null;
  function doLargeCalc() {
    var invalid = (L.upBoard + L.upAlight + L.downBoard + L.downAlight + L.xferBoard + L.xferAlight) === 0;
    var req = $('span[data-req]', $('#large-inputs'));
    if (req) req.classList.toggle('hidden', !invalid);
    lastLarge = calcLarge();
    renderLargeResults(lastLarge, invalid);
    setStatus($('#large-status'), invalid
      ? '计算完成,但缺少必填的客流资料(结果无意义)'
      : '计算完成(与北京站算例同口径)', invalid);
    appStatus('大系统负荷计算完成:' + (invalid ? '缺少客流' : '总制冷 ' + fmt(lastLarge.E159, 2) + ' kW'));
  }

  // =========================================================================
  // 5. 拾取空间 / 默认参数 / 报告
  // =========================================================================
  var SPACES = [
    { name: '站厅层-公共区', area: 2000, height: 4.9, length: 101 },
    { name: '站台层-公共区', area: 1620, height: 4.65, length: 141 },
    { name: '设备区-管理用房', area: 60, height: 4.5, length: 30 },
    { name: '设备区-弱电用房', area: 45, height: 4.5, length: 26 },
    { name: '设备区-强电用房', area: 80, height: 4.5, length: 34 },
    { name: '设备区-环控机房', area: 220, height: 6, length: 42 }
  ];
  var pickedSpaces = [];

  function openPickSpace() {
    var ul = $('#pickspace-list');
    ul.innerHTML = SPACES.map(function (s, i) {
      return '<li data-i="' + i + '">' + s.name + ' — ' + s.area + ' m² / 层高 ' + s.height + ' m</li>';
    }).join('');
    pickedSpaces = [];
    $$('#pickspace-list li').forEach(function (li) {
      li.addEventListener('click', function () {
        var i = +li.getAttribute('data-i');
        var pos = pickedSpaces.indexOf(i);
        if (pos >= 0) { pickedSpaces.splice(pos, 1); li.classList.remove('sel'); }
        else { pickedSpaces.push(i); li.classList.add('sel'); }
        $('#pickspace-info').textContent = '已选 ' + pickedSpaces.length + ' 个空间' +
          (pickedSpaces.length ? ':' + pickedSpaces.map(function (x) { return SPACES[x].name; }).join('、') : '');
      });
    });
    $('#pickspace-info').textContent = '已选 0 个空间(Esc 或"取消拾取"放弃)';
    openWin('win-pickspace');
  }

  function applyPickedSpaces() {
    if (!pickedSpaces.length) { setStatus($('#large-status'), '未选择空间,保持原值', true); return; }
    pickedSpaces.forEach(function (i) {
      var s = SPACES[i];
      if (s.name.indexOf('站厅') >= 0) { L.hallArea = s.area; L.hallHeight = s.height; L.hallLength = s.length; }
      else if (s.name.indexOf('站台') >= 0) { L.platArea = s.area; }
    });
    syncLargeSheet();
    closeWin($('#win-pickspace'));
    setStatus($('#large-status'), '已从模型空间读取:站厅 ' + fmt(L.hallArea, 1) + ' m² / 站台 ' + fmt(L.platArea, 1) + ' m²');
    appStatus('已拾取空间并回填几何参数');
  }

  function syncLargeSheet() {
    $$('#large-inputs input[data-k]').forEach(function (inp) {
      var k = inp.getAttribute('data-k');
      inp.value = L[k];
    });
  }

  function openDefaults() {
    var spec = LARGE_SPEC.slice(2, 8); // 三~七节
    var host = $('#defaults-inputs');
    host.innerHTML = spec.map(function (sec) {
      return '<div class="group"><div class="gtitle">' + sec.title +
        '</div><div class="grid3">' + sec.fields.map(function (f) {
          return '<div class="row tight"><label>' + f.label + '</label>' +
            '<input type="number" data-dk="' + f.k + '" value="' + L[f.k] + '"></div>';
        }).join('') + '</div></div>';
    }).join('');
    openWin('win-defaults');
  }
  function applyDefaults() {
    $$('#defaults-inputs input[data-dk]').forEach(function (inp) {
      L[inp.getAttribute('data-dk')] = num(inp.value);
    });
    syncLargeSheet();
    largeDirty = true;
    closeWin($('#win-defaults'));
    setStatus($('#large-status'), '默认参数已更新,可重新计算');
  }
  function restoreDefaults() {
    Object.keys(DEFAULT_L).forEach(function (k) { L[k] = DEFAULT_L[k]; });
    syncLargeSheet();
    largeDirty = true;
    setStatus($('#large-status'), '已恢复公式文档默认值(客流需重新填写)');
  }

  function buildReport(which) {
    var t = [];
    if (which === 'large' && lastLarge) {
      var r = lastLarge;
      t.push('大系统负荷计算书(原型导出)');
      t.push('======================================');
      t.push('站厅公共区高峰客流: ' + fmt(r.C39, 1) + ' 个/min (C39)');
      t.push('站台公共区高峰客流: ' + fmt(r.C40, 1) + ' 个/min (C40)');
      t.push('站厅冷负荷合计: ' + fmt(r.D107, 2) + ' kW (D107)');
      t.push('站台冷负荷合计: ' + fmt(r.E107, 2) + ' kW (E107)');
      t.push('总送风量: ' + fmt(r.C125, 0, true) + ' m³/h (C125)');
      t.push('实际新风量: ' + fmt(r.A136, 0, true) + ' m³/h (A136);新风比 ' + fmt(r.B136 * 100, 1) + ' % (B136)');
      t.push('总回风量: ' + fmt(r.E136, 0, true) + ' m³/h (E136)');
      t.push('总制冷量: ' + fmt(r.E159, 2) + ' kW (E159)');
      t.push('站厅排烟(计算风量): ' + fmt(r.C171, 0, true) + ' m³/h (C171)');
      t.push('站台排烟(计算风量): ' + fmt(r.D171, 0, true) + ' m³/h (D171)');
      t.push('单台组合式空调机组: ' + fmt(r.A165, 0, true) + ' m³/h / ' + fmt(r.B165, 2) + ' kW (A165/B165)');
      t.push('单台回排风机: ' + fmt(r.C178, 0, true) + ' m³/h (C178)');
      t.push('单台排烟风机: ' + fmt(r.E178, 0, true) + ' m³/h (E178)');
      t.push('');
      t.push('注:原型演示数据;正式版由 HVACIDA.Core 生成并支持 Excel/PDF 导出。');
    } else if (which === 'allair' && lastAllAir) {
      t.push('小系统(全空气一次回风)计算书(原型导出)');
      t.push('======================================');
      lastAllAir.rows.forEach(function (row) {
        t.push(row.name + ': 总冷负荷 ' + fmt(row.totalKw, 2) + ' kW,通风量 ' +
          fmt(row.vent, 0, true) + ' m³/h,新风量 ' + fmt(row.fresh, 0, true) + ' m³/h');
      });
      t.push('');
      t.push('合计: ' + fmt(lastAllAir.sumKw, 2) + ' kW');
    } else {
      t.push('请先执行【计算】,再导出计算书。');
    }
    $('#report-text').textContent = t.join('\n');
    setStatus($('#report-status'), '');
    openWin('win-report');
  }

  // =========================================================================
  // 6. 小系统:六类类型(Ribbon 一级按钮)+ 全空气一次回风
  // =========================================================================
  var SYSTYPES = [
    { id: 'allair', name: '全空气一次回风系统', impl: true, note: '按空间:照明/人员/设备负荷 + 除热通风量 + 换气次数 + 新风量;输出柜式空调机组与回排风机选型。' },
    { id: 'vrf', name: '多联机 + 新风系统', impl: false, note: '基础参数(夏季/过渡季温度、空间物理参数、负荷指标、人数、换气次数)→ 负荷/通风量 → 新风机组/送排风机/多联机外机选型。' },
    { id: 'exhaust', name: '排风系统', impl: false, sub: ['环控机房通风系统', '卫生间排风系统'], note: '按换气次数确定通风量;环控机房与卫生间分别取指标。' },
    { id: 'smoke', name: '排烟系统', impl: false, note: '按防烟分区面积与换气次数计算排烟量,风机选型含 1.2 系数。' },
    { id: 'sesmoke', name: '送风排风排烟系统', impl: false, note: '送/排/排烟共用系统,需风量叠加与阀门切换逻辑。' },
    { id: 'press', name: '加压送风系统', impl: false, note: '楼梯间/前室加压送风量,按规范查表与门洞风速校核。' }
  ];

  /** 点击 Ribbon 小系统类型按钮:全空气直接进计算窗,其余给待实现说明。 */
  function openSmallType(id) {
    var s = SYSTYPES.filter(function (x) { return x.id === id; })[0];
    if (!s) return;
    if (s.impl) {
      renderAllAir();
      openWin('win-allair');
      appStatus('已打开:小系统 — ' + s.name);
    } else {
      $('#todo-msg').innerHTML = '<b>' + s.name + '</b>尚未实现(骨架阶段)。<br>' +
        '<span style="color:#5A5A5A;">计算要点:' + s.note + '</span>' +
        (s.sub ? '<br><span style="color:#5A5A5A;">子类型:' + s.sub.join(' / ') + '</span>' : '');
      openWin('win-todo');
      appStatus('小系统类型「' + s.name + '」尚未实现(骨架阶段)');
    }
  }

  // 小系统 — 全空气一次回风
  var allAirSpaces = [
    { name: '设备区-管理用房', area: 60, height: 4.5, wallLen: 32.5, roofArea: 60, equip: 20, light: 15, occupants: 10, ach: 6, indoorT: 26, dT: 10, freshPP: 30 }
  ];
  var allAirSel = 0, lastAllAir = null;

  function renderAllAir() {
    $('#allair-spaces').innerHTML = allAirSpaces.map(function (s, i) {
      return '<div data-i="' + i + '" class="' + (i === allAirSel ? 'sel' : '') + '">' + s.name + '</div>';
    }).join('');
    $$('#allair-spaces div').forEach(function (d) {
      d.addEventListener('click', function () { allAirSel = +d.getAttribute('data-i'); renderAllAir(); });
    });
    var s = allAirSpaces[allAirSel];
    if (!s) { $('#allair-detail').innerHTML = '<div class="group"><div class="gtitle">空间详情</div>请从左侧添加空间</div>'; return; }
    $('#allair-detail').innerHTML =
      '<div class="group"><div class="gtitle">物理参数(模型获取)</div><div class="grid1">' +
      '<div class="row tight"><label>面积 m²</label><input type="number" value="' + s.area + '" readonly></div>' +
      '<div class="row tight"><label>层高 m</label><input type="number" value="' + s.height + '" readonly></div>' +
      '<div class="row tight"><label>与土壤接触外墙长度 m</label>' +
      '<input type="number" id="aa-wall" value="' + s.wallLen + '" style="width:80px;">' +
      '<button data-act="aa-pickwall" style="min-width:62px;">拾取…</button></div>' +
      '<div class="row tight"><label>与土壤接触屋顶面积 m²</label><input type="number" id="aa-roof" value="' + s.roofArea + '"></div>' +
      '</div></div>' +
      '<div class="group"><div class="gtitle">负荷与通风参数(可编辑)</div><div class="grid1">' +
      '<div class="row tight"><label>设备冷负荷 W/m²</label><input type="number" id="aa-equip" value="' + s.equip + '"></div>' +
      '<div class="row tight"><label>照明指标 W/m²</label><input type="number" id="aa-light" value="' + s.light + '"></div>' +
      '<div class="row tight"><label>预测房间人数 人</label><input type="number" id="aa-occ" value="' + s.occupants + '"></div>' +
      '<div class="row tight"><label>换气次数 次/h</label><input type="number" id="aa-ach" value="' + s.ach + '"></div>' +
      '<div class="row tight"><label>室内温度 ℃</label><input type="number" id="aa-tin" value="' + s.indoorT + '"></div>' +
      '<div class="row tight"><label>送风温差 ℃</label><input type="number" id="aa-dt" value="' + s.dT + '"></div>' +
      '</div></div>';
    $$('#allair-detail input').forEach(function (inp) {
      inp.addEventListener('input', function () { collectAllAir(); });
    });
  }
  function collectAllAir() {
    var s = allAirSpaces[allAirSel];
    if (!s) return;
    var g = function (id, d) { var el = document.getElementById(id); return el ? num(el.value) : d; };
    s.wallLen = g('aa-wall', s.wallLen);
    s.roofArea = g('aa-roof', s.roofArea);
    s.equip = g('aa-equip', s.equip);
    s.light = g('aa-light', s.light);
    s.occupants = g('aa-occ', s.occupants);
    s.ach = g('aa-ach', s.ach);
    s.indoorT = g('aa-tin', s.indoorT);
    s.dT = g('aa-dt', s.dT);
  }

  var WALLS = [
    { name: '外墙 W-1(与土壤接触)', len: 12.5 },
    { name: '外墙 W-2(与土壤接触)', len: 8.0 },
    { name: '外墙 W-3(与土壤接触)', len: 6.5 },
    { name: '外墙 W-4(与土壤接触)', len: 5.5 }
  ];
  var pickedWalls = [];
  function openPickWall() {
    $('#pickwall-list').innerHTML = WALLS.map(function (w, i) {
      return '<li data-i="' + i + '">' + w.name + ' — ' + w.len.toFixed(2) + ' m</li>';
    }).join('');
    pickedWalls = [];
    $$('#pickwall-list li').forEach(function (li) {
      li.addEventListener('click', function () {
        var i = +li.getAttribute('data-i');
        var p = pickedWalls.indexOf(i);
        if (p >= 0) { pickedWalls.splice(p, 1); li.classList.remove('sel'); } else { pickedWalls.push(i); li.classList.add('sel'); }
        updateWallInfo();
      });
    });
    updateWallInfo();
    openWin('win-pickwall');
  }
  function updateWallInfo() {
    var total = pickedWalls.reduce(function (a, i) { return a + WALLS[i].len; }, 0);
    $('#pickwall-info').textContent = '已选 ' + pickedWalls.length + ' 段,共 ' + total.toFixed(2) + ' m';
  }

  function calcAllAir() {
    collectAllAir();
    var rows = [], sum = 0;
    allAirSpaces.forEach(function (s) {
      var light = s.area * s.light;
      var equip = s.area * s.equip;
      var people = s.occupants * 61;
      var totalW = light + equip + people;
      var vent = s.dT > 0 ? totalW * 3600 / (1.2 * 1.01 * 1000 * s.dT) : 0;
      var achVent = s.area * s.height * s.ach;
      var actual = Math.max(vent, achVent);
      var fresh = Math.min(s.occupants * s.freshPP, actual);
      rows.push({ name: s.name, light: light, people: people, equip: equip, totalKw: totalW / 1000, vent: actual, fresh: fresh });
      sum += totalW / 1000;
    });
    lastAllAir = { rows: rows, sumKw: sum };
    $('#allair-results').innerHTML =
      '<div class="card"><div class="chead">计算结果</div><div class="cbody">' +
      rows.map(function (r) {
        return '<div class="kv strong"><span class="k">' + r.name + ' 总冷负荷</span><span class="v">' + fmt(r.totalKw, 2) +
          '</span><span class="u">kW</span></div>' +
          '<div class="kv"><span class="k">通风量 / 新风量</span><span class="v">' + fmt(r.vent, 0, true) + ' / ' + fmt(r.fresh, 0, true) + '</span><span class="u">m³/h</span></div>';
      }).join('') +
      '<div class="kv strong"><span class="k">合计</span><span class="v">' + fmt(sum, 2) + '</span><span class="u">kW</span></div>' +
      '</div></div>' +
      '<div class="card"><div class="chead">设备选型</div><div class="cbody" style="color:#5A5A5A;">' +
      '柜式空调机组 / 回排风机选型:待接入选型规则库(需求 2.2.3.2)。</div></div>';
    setStatus($('#allair-status'), '计算完成:' + allAirSpaces.length + ' 个空间,合计 ' + fmt(sum, 2) + ' kW');
  }

  // =========================================================================
  // 7. AI 问答(原型:关键词应答)
  // =========================================================================
  var AI_QA = [
    { k: ['送风温差', '温差'], a: '地铁公共区常用送风温差 8~10 ℃(站厅取 10 ℃);站厅与站台送风温度须一致——本项目由站厅送风温度统一确定,站台不单独计算送风温度。' },
    { k: ['排烟', '风机选型'], a: '排烟:计算风量 = 公共区面积 × 60 m³/(h·m²);选型风量 = 计算风量 × 1.2(等效防烟分区面积 × 72)。' },
    { k: ['新风'], a: '空调季新风量指标按 20 m³/(h·人) 取值,并满足不小于总送风量的 10%;实际新风量取两者较大值。' },
    { k: ['焓湿', '焓值', '露点'], a: '焓湿计算:露点相对湿度 95%;饱和含湿量按公式文档 7 次多项式;焓 h = 1.01t + (2500 + 1.84t)·d/1000 + 0.4。' },
    { k: ['客流', '集群'], a: '高峰客流 = (上客 + 下客) × 集群系数(0.89) × 超高峰小时系数;停站时间默认上车 2 min / 下车 1.5 min。' },
    { k: ['屏蔽门'], a: '屏蔽门:传热量 = K·h·L·ΔT·安全系数(默认 3.2 / 3 m / 292 m / 8 ℃ / 1.5);漏风量站厅 30 kW、站台 45 kW。' }
  ];
  function aiAnswer(q) {
    for (var i = 0; i < AI_QA.length; i++) {
      for (var j = 0; j < AI_QA[i].k.length; j++) {
        if (q.indexOf(AI_QA[i].k[j]) >= 0) return AI_QA[i].a;
      }
    }
    return '原型阶段仅支持少量本地规则应答(可试:送风温差 / 排烟 / 新风 / 焓湿 / 客流 / 屏蔽门)。正式版将接入 AI 服务接口(需求 4.1),支持规范查询与设计建议。';
  }
  function aiAppend(who, text) {
    var log = $('#ai-log');
    var color = who === 'me' ? '#0078D7' : '#333';
    var label = who === 'me' ? '我:' : 'AI:';
    log.innerHTML += '<div style="color:' + color + ';"><b>' + label + '</b> ' + text + '</div>';
    log.scrollTop = log.scrollHeight;
  }

  // =========================================================================
  // 8. 交互流程 / 交互逻辑表(由数据生成)
  // =========================================================================
  function buildFlow() {
    function node(text, cls) { return '<div class="node ' + (cls || '') + '">' + text + '</div>'; }
    function down() { return '<div class="arrow-down"></div>'; }
    function lane(inner) { return '<div class="lane">' + inner + '</div>'; }

    var html = '<h2>HVACIDA 交互流程(原型)</h2>' +
      '<div class="legend">' +
      '<span><i style="background:#DCEBF9;border-color:#0078D7;"></i>入口</span>' +
      '<span><i style="background:#EFF6FF;"></i>功能板块</span>' +
      '<span><i style="background:#FFF;border-style:dashed;"></i>窗口</span>' +
      '<span><i style="background:#F3FFF3;"></i>操作</span>' +
      '<span><i style="background:#F5F5F5;border-style:dotted;"></i>未实现</span>' +
      '</div><div class="flow">';

    html += lane(node('Revit 2020 → Ribbon「HVACIDA」页', 'root')) + down();
    html += lane(
      node('项目信息<br><small>2.1</small>', 'mod') +
      node('大系统负荷计算<br><small>2.2.3.1</small>', 'mod') +
      node('小系统负荷计算<br><small>2.2.3.2 · 六类按钮</small>', 'mod') +
      node('风系统水力<br><small>2.3</small>', 'mod') +
      node('水系统水力<br><small>2.4</small>', 'mod') +
      node('出图<br><small>2.6</small>', 'mod') +
      node('AI问答<br><small>2.7</small>', 'mod')
    ) + down();

    html += '<h3>① 项目信息</h3>' + lane(
      node('项目信息窗口', 'nd-win') + '<div class="arrow-right"></div>' +
      node('手动填写 / Excel 模板导入', 'act') + '<div class="arrow-right"></div>' +
      node('校验(必填/数值范围)', 'act') + '<div class="arrow-right"></div>' +
      node('确定 → 写入 .rvt 全局参数', 'act')
    ) + down();

    html += '<h3>② 负荷及通风计算(Ribbon 两个一级入口,2026-09-11 调整)</h3>';
    html += lane(
      node('Ribbon:大系统负荷计算', 'mod') + '<div class="arrow-right"></div>' +
      node('大系统窗口<br>左:输入七节 / 右:结果卡片', 'nd-win') + '<div class="arrow-right"></div>' +
      node('拾取空间 → 面积/层高/长度', 'act') + '<div class="arrow-right"></div>' +
      node('默认参数…(子窗)', 'act') + '<div class="arrow-right"></div>' +
      node('计算 → 客流/负荷/风量/制冷/选型', 'act') + '<div class="arrow-right"></div>' +
      node('导出计算书', 'act')
    ) + down();
    html += lane(
      node('Ribbon:小系统六类按钮', 'mod') + '<div class="arrow-right"></div>' +
      node('全空气一次回风窗口<br>(其余五类:待实现提示)', 'nd-win') + '<div class="arrow-right"></div>' +
      node('空间列表 → 详情(拾取墙体…)', 'act') + '<div class="arrow-right"></div>' +
      node('计算 → 负荷/通风量/新风/选型', 'act')
    ) + down();

    html += '<h3>③ 水力 / 出图 / AI(骨架占位)</h3>' + lane(
      node('风系统水力: 系统树 → 参数表 → 计算/平衡', 'sys') +
      node('水系统水力: 环路 → 参数 → 计算/选型', 'sys') +
      node('出图: 模板 → 标注/图例 → 批量出图', 'sys') +
      node('AI问答: 提问 → 规则应答(AI 服务待接)', 'sys')
    ) + down();

    html += '<h3>④ 公用交互(所有窗口一致)</h3>' + lane(
      node('确定 / 取消 / Esc', 'act') +
      node('必填与范围校验(行内标红)', 'act') +
      node('状态栏反馈(成功绿 / 失败红)', 'act') +
      node('Window Owner = Revit 主窗口,居中显示', 'act')
    );
    html += '</div>';
    $('#page-flow').innerHTML = html;
  }

  function buildLogic() {
    var rows = [
      ['点击 Ribbon 按钮', 'HVACIDA 页', '打开对应模态窗(Owner=Revit 主窗口, CenterOwner)', '窗口未打开/报错'],
      ['点击「大系统负荷计算」', 'Ribbon 一级按钮', '直接打开大系统窗(不再经过入口窗)', '—'],
      ['点击「小系统」六类按钮', 'Ribbon 一级按钮(一行六键·图标+文字)', '全空气→直接进计算窗;其余五类→待实现提示窗(说明计算要点)', '未实现类型:不伪装可用,状态栏红字'],
      ['窗口宽度变窄', 'Ribbon 自适应(始终一行六键)', '≤1120px 隐藏图标 → ≤980px 收窄至 56px → 再窄则整条 Ribbon 横向滚动', '不折行、不堆叠;每个标签最多 2 行,不截断'],
      ['从模型拾取空间', '大系统 / 小系统', '隐藏窗口 → PickObject 选择 → 回填面积/层高/长度(只读底 → 可编辑)', 'Esc 取消保留原值;空间缺参数则标红提示'],
      ['拾取墙体(多选)', '全空气一次回风 · 外墙长度', '多次点选 → 实时累计长度 → 应用总长', '未选中时提示"未选择,保持原值"'],
      ['默认参数…', '大系统底栏', '打开子窗(第三~七节)→ 确定回写 → 提示可重算', '取消不回写'],
      ['恢复默认', '大系统底栏', '二次确认后恢复公式文档默认值(客流清零)', '取消则不变'],
      ['计 算', '大系统 / 小系统', '校验必填 → 执行公式链 → 右侧结果卡片', '客流为 0:结果区提示"缺少必填参数",不弹系统框'],
      ['导出计算书', '大系统 / 小系统', '生成报告文本 → 预览窗(可复制/导出)', '未计算时提示先计算'],
      ['确定', '所有窗口', '保存参数 / 写入 .rvt 全局参数(项目信息)→ 关闭', '校验失败阻止关闭并定位首个错误'],
      ['取消 / Esc', '所有窗口', '关闭不保存;有未保存修改时弹确认', '—'],
      ['未实现模块', '水力 / 出图 / AI', '灰字说明 + 状态栏红字,按钮禁用或仅演示', '不伪装可用'],
      ['AI 提问', 'AI 问答窗', '输入问题 → 发送 → 追加对话(原型:规则应答)', '无匹配时给出能力范围说明']
    ];
    var html = '<h2>交互逻辑表</h2>' +
      '<table class="grid"><tr><th style="width:150px;">操作</th><th style="width:190px;">位置</th>' +
      '<th>触发与反馈</th><th style="width:240px;">异常/边界处理</th></tr>';
    rows.forEach(function (r) {
      html += '<tr><td>' + r[0] + '</td><td>' + r[1] + '</td><td>' + r[2] + '</td><td>' + r[3] + '</td></tr>';
    });
    html += '</table>' +
      '<h2 style="margin-top:16px;">数值与单位规范(节选)</h2>' +
      '<table class="grid"><tr><th>量</th><th>单位</th><th>小数位</th><th>显示示例</th></tr>' +
      '<tr><td>温度 / 温差</td><td>℃</td><td class="num">1</td><td class="num">19.0</td></tr>' +
      '<tr><td>面积</td><td>m²</td><td class="num">1</td><td class="num">2000.0</td></tr>' +
      '<tr><td>风量</td><td>m³/h</td><td class="num">0(千分位)</td><td class="num">87,768</td></tr>' +
      '<tr><td>冷负荷 / 制冷量</td><td>kW</td><td class="num">2</td><td class="num">381.60</td></tr>' +
      '<tr><td>焓</td><td>kJ/kg</td><td class="num">2</td><td class="num">49.72</td></tr>' +
      '<tr><td>含湿量</td><td>g/kg</td><td class="num">2</td><td class="num">11.89</td></tr>' +
      '</table>';
    $('#page-logic').innerHTML = html;
  }

  // =========================================================================
  // 9. 事件绑定:大系统 / 小系统 / 项目信息 等
  // =========================================================================
  document.addEventListener('click', function (e) {
    var t = e.target.closest ? e.target.closest('[data-act]') : null;
    if (!t) return;
    var act = t.getAttribute('data-act');
    switch (act) {
      // 项目信息
      case 'proj-import': {
        PROJ.basic.forEach(function (f) { if (f.def != null) $('[data-pk=' + f.k + ']').value = f.def; });
        setStatus($('#proj-status'), '已导入模板:项目信息模板.xlsx(模拟,12 个字段已回填)');
        break;
      }
      case 'proj-template':
        setStatus($('#proj-status'), '已导出空模板:%AppData%\\HVACIDA\\项目信息模板.xlsx(模拟)');
        break;
      case 'proj-ok':
        closeWin($('#win-project'));
        appStatus('项目信息已以全局参数写入当前 .rvt(模拟)');
        break;
      // 负荷 hub
      // 负荷计算:Ribbon 一级按钮直接进入(大系统 data-open="win-large";小系统六类 data-act="small-type")
      // 大系统
      case 'large-pick': openPickSpace(); appStatus('拾取空间:请在列表中选择(模拟 Revit PickObject)'); break;
      case 'large-defaults': openDefaults(); break;
      case 'large-reset': askConfirm('确认恢复公式文档默认参数?(已填写的客流将清零)', restoreDefaults); break;
      case 'large-sample':
        Object.keys(SAMPLE).forEach(function (k) { L[k] = SAMPLE[k]; });
        syncLargeSheet(); largeDirty = true;
        setStatus($('#large-status'), '已载入北京算例参数,点【计 算】查看 381.60 kW');
        break;
      case 'large-calc': doLargeCalc(); break;
      case 'large-report': buildReport('large'); break;
      case 'large-ok':
        closeWin($('#win-large')); largeDirty = false;
        appStatus('大系统参数已保存(模拟写入全局参数)');
        break;
      // 拾取空间
      case 'pickspace-apply': applyPickedSpaces(); break;
      // 默认参数
      case 'defaults-ok': applyDefaults(); break;
      case 'defaults-restore': restoreDefaults(); break;
      // 小系统(Ribbon 六类一级按钮 → 直达计算窗 / 待实现提示)
      case 'small-type':
        openSmallType(t.getAttribute('data-type'));
        break;
      case 'allair-add':
        allAirSpaces.push({ name: '设备区-强电用房', area: 80, height: 4.5, wallLen: 28, roofArea: 80, equip: 25, light: 15, occupants: 6, ach: 8, indoorT: 28, dT: 10, freshPP: 30 });
        allAirSel = allAirSpaces.length - 1; renderAllAir(); appStatus('已从模型添加空间(模拟)');
        break;
      case 'allair-del':
        if (allAirSpaces.length > 1) { allAirSpaces.splice(allAirSel, 1); allAirSel = 0; renderAllAir(); }
        break;
      case 'aa-pickwall': openPickWall(); break;
      case 'pickwall-apply': {
        var total = pickedWalls.reduce(function (a, i) { return a + WALLS[i].len; }, 0);
        var el = document.getElementById('aa-wall');
        if (el && pickedWalls.length) { el.value = total.toFixed(2); collectAllAir(); }
        closeWin($('#win-pickwall'));
        setStatus($('#allair-status'), pickedWalls.length ? ('已应用墙体总长 ' + total.toFixed(2) + ' m') : '未选择墙体,保持原值');
        break;
      }
      case 'allair-calc': calcAllAir(); break;
      case 'allair-report': buildReport('allair'); break;
      case 'allair-ok': closeWin($('#win-allair')); appStatus('小系统参数已保存(模拟)'); break;
      // 报告
      case 'report-copy': {
        var text = $('#report-text').textContent;
        if (navigator.clipboard && navigator.clipboard.writeText) {
          navigator.clipboard.writeText(text).then(function () { setStatus($('#report-status'), '已复制到剪贴板'); },
            function () { setStatus($('#report-status'), '复制失败,请手动选择复制', true); });
        } else { setStatus($('#report-status'), '当前环境不支持自动复制,请手动选择', true); }
        break;
      }
      // AI
      case 'ai-send': {
        var inp = $('#ai-input');
        var q = (inp.value || '').trim();
        if (!q) return;
        aiAppend('me', q);
        aiAppend('ai', aiAnswer(q));
        inp.value = '';
        break;
      }
    }
  });

  // 回车发送 AI
  document.addEventListener('keydown', function (e) {
    if (e.key === 'Enter' && document.activeElement && document.activeElement.id === 'ai-input') {
      $('[data-act=ai-send]').click();
    }
  });

  // 项目信息表单渲染
  function renderProject() {
    function inputHtml(f, prefix) {
      var id = 'data-' + prefix + '="' + f.k + '"';
      if (f.type === 'select') {
        return '<div class="row"><label>' + f.label + '</label><select ' + id + '>' +
          f.options.map(function (o) { return '<option>' + o + '</option>'; }).join('') + '</select></div>';
      }
      var isText = f.type === 'text';
      return '<div class="row"><label>' + f.label + '</label><input type="' + (isText ? 'text' : 'number') +
        '" ' + (isText ? 'class="txt-lg"' : '') + ' ' + id + ' value="' + (f.def == null ? '' : f.def) + '"></div>';
    }
    $('#proj-basic').innerHTML = PROJ.basic.map(function (f) { return inputHtml(f, 'pk'); }).join('');
    $('#proj-out-large').innerHTML = PROJ.outLarge.map(function (f) { return inputHtml(f, 'pk'); }).join('');
    $('#proj-out-small').innerHTML = PROJ.outSmall.map(function (f) { return inputHtml(f, 'pk'); }).join('');
    $('#proj-in').innerHTML = PROJ.indoor.map(function (f) { return inputHtml(f, 'pk'); }).join('');
  }

  // 深链接演示:#demo-large / #demo-project / #demo-defaults / #demo-todo / #demo-allair / #demo-report / #demo-ai
  function openDemo(name) {
    switch (name) {
      case 'project':
        openWin('win-project');
        break;
      case 'large':
        openWin('win-large');
        Object.keys(SAMPLE).forEach(function (k) { L[k] = SAMPLE[k]; });
        syncLargeSheet();
        doLargeCalc();
        break;
      case 'defaults':
        openWin('win-large');
        openDefaults();
        break;
      case 'todo':
        openSmallType('vrf');
        break;
      case 'allair':
        renderAllAir();
        openWin('win-allair');
        calcAllAir();
        break;
      case 'report':
        if (!lastLarge) {
          Object.keys(SAMPLE).forEach(function (k) { L[k] = SAMPLE[k]; });
          syncLargeSheet();
          doLargeCalc();
        }
        buildReport('large');
        break;
      case 'ai':
        openWin('win-ai');
        aiAppend('me', '排烟风机怎么选?');
        aiAppend('ai', aiAnswer('排烟风机怎么选?'));
        break;
      default:
        break;
    }
    appStatus('深链接演示:' + name);
  }

  // =========================================================================
  // 10. 初始化
  // =========================================================================
  // 把所有窗口挂到模型视图容器内:窗口坐标相对画布,不再覆盖 Ribbon 区域
  (function reparentWindows() {
    var c = $('#canvas');
    $$('.win').forEach(function (w) { c.appendChild(w); });
  })();

  renderProject();
  renderLargeInputs();
  renderLargeResults(null);
  buildFlow();
  buildLogic();
  aiAppend('ai', '你好,我是 HVACIDA 设计助手(原型)。可试问:送风温差取多少?排烟风机怎么选?新风量怎么定?');

  var v0 = (location.hash || '#ui').slice(1);
  if (v0.indexOf('demo-') === 0) {
    setView('ui');
    openDemo(v0.slice(5));
  } else {
    setView(['ui', 'flow', 'logic'].indexOf(v0) >= 0 ? v0 : 'ui');
  }
})();
