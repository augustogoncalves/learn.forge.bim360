/////////////////////////////////////////////////////////////////////
// Copyright (c) Autodesk, Inc. All rights reserved
// Written by Forge Partner Development
//
// Permission to use, copy, modify, and distribute this software in
// object code form for any purpose and without fee is hereby granted,
// provided that the above copyright notice appears in all copies and
// that both that copyright notice and the limited warranty and
// restricted rights notice below appear in all supporting
// documentation.
//
// AUTODESK PROVIDES THIS PROGRAM "AS IS" AND WITH ALL FAULTS.
// AUTODESK SPECIFICALLY DISCLAIMS ANY IMPLIED WARRANTY OF
// MERCHANTABILITY OR FITNESS FOR A PARTICULAR USE.  AUTODESK, INC.
// DOES NOT WARRANT THAT THE OPERATION OF THE PROGRAM WILL BE
// UNINTERRUPTED OR ERROR FREE.
/////////////////////////////////////////////////////////////////////

$(document).ready(function () {
  // first, check if current visitor is signed in
  jQuery.ajax({
    url: '/api/forge/oauth/token',
    success: function (res) {
      // yes, it is signed in...
      $('#signOut').show();
      $('#refreshHubs').show();

      // prepare sign out
      $('#signOut').click(function () {
        $('#hiddenFrame').on('load', function (event) {
          location.href = '/api/forge/oauth/signout';
        });
        $('#hiddenFrame').attr('src', 'https://accounts.autodesk.com/Authentication/LogOut');
        // learn more about this signout iframe at
        // https://forge.autodesk.com/blog/log-out-forge
      })

      // and refresh button
      $('#refreshHubs').click(function () {
        prepareListOfProjects();
      });

      // finally:
      prepareListOfProjects();
      showUser();
    }
  });

  $('#autodeskSigninButton').click(function () {
    jQuery.ajax({
      url: '/api/forge/oauth/url',
      success: function (url) {
        location.href = url;
      }
    });
  })
});

function prepareListOfProjects() {
  var list = $('#userProjects');
  list.html('<div class="loadingspinner"></div>');
  jQuery.ajax({
    url: '/api/forge/datamanagement/projects',
    success: function (projects) {
      var listGroup = $('<div/>').addClass('list-group').appendTo(list);
      projects.forEach(function (item) {
        var projectItem = $('<a/>').attr('href', '#').addClass('list-group-item').attr('hubId', item.hub.id).attr('projectId', item.project.id).appendTo(listGroup);
        $('<h7/>').addClass('list-group-item-heading').appendTo(projectItem).text(item.project.name);
        $('<p/>').addClass('list-group-item-text').appendTo(projectItem).text(item.hub.name);
      });

      $('.list-group-item').on('click', function () {
        var $this = $(this);
        $this.toggleClass('active');

        updateSelected();
      })
    }
  });
}

function updateSelected() {
  var projectIds = [];
  $('.list-group-item').each(function (i, item) {
    if (!$(item).hasClass('active')) return;
    projectIds.push({ hubId: $(item).attr('hubId'), projectId: $(item).attr('projectId') });
  })
  var issues = [];
  projectIds.forEach(function (project) {
    $("#charts").append('<div id="loadingdata" class="loadingspinner"></div>');
    jQuery.ajax({
      url: 'api/forge/bim360/hubs/' + project.hubId + '/projects/' + project.projectId + '/quality-issues',
      success: function (response) {
        issues = issues.concat(response);
        $("#loadingdata").remove();
        $("#chartscontainer").empty();
        createChart('issueStatus', 'Issues by Status', issues, 'attributes.status');
        createChart('issueOwner', 'Issues by Owner', issues, 'attributes.owner');
        createChart('issueAssignedTo', 'Issues by Assigned To', issues, 'attributes.assigned_to');
        createChart('issueAnsweredBy', 'Issues by Answered By', issues, 'attributes.answered_by');
        createChart('issueRootCause', 'By root cause', issues, 'attributes.root_cause');
      }
    })
  })
}

function createChart(name, title, data, attribute) {
  $("#chartscontainer").append('<li class="flex-item"><canvas id="' + name + '" width="350" height="350"></canvas></li>');
  var chartData = countOccurrences(data, attribute);
  var chartColors = generateColors(Object.keys(chartData).length);

  var ctx = document.getElementById(name).getContext('2d');
  var chart = new Chart(ctx, {
    type: 'pie',
    data: {
      labels: Object.keys(chartData),
      datasets: [{
        data: Object.values(chartData),
        backgroundColor: chartColors.background,
        borderColor: chartColors.borders,
        borderWidth: 1
      }]
    },
    options: {
      title: {
        display: true,
        text: title
      }
    }
  });
}

function countOccurrences(data, attribute) {
  var res = {};
  data.forEach(function (entry) {
    var value = (attribute.split('.').reduce((a, v) => a[v], entry) || "N/A");
    if (res[value] == null) res[value] = 0;
    res[value]++
  })
  return res;
}

function generateColors(count) {
  var background = []; var borders = [];
  for (var i = 0; i < count; i++) {
    var r = Math.round(Math.random() * 255); var g = Math.round(Math.random() * 255); var b = Math.round(Math.random() * 255);
    background.push('rgba(' + r + ', ' + g + ', ' + b + ', 0.2)');
    borders.push('rgba(' + r + ', ' + g + ', ' + b + ', 0.2)');
  }
  return { background: background, borders: borders };
}

function showUser() {
  jQuery.ajax({
    url: '/api/forge/user/profile',
    success: function (profile) {
      var img = '<img src="' + profile.picture + '" height="30px">';
      $('#userInfo').html(img + profile.name);
    }
  });
}