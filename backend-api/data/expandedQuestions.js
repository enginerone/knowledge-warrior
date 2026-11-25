const programmingAndWeb = require('./questionsProgrammingWeb');
const networkingAndDatabases = require('./questionsNetworkingDatabases');
const securityAndSystems = require('./questionsSecuritySystems');

module.exports = [
  ...programmingAndWeb,
  ...networkingAndDatabases,
  ...securityAndSystems,
];
