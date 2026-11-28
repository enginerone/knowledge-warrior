const programmingAndWeb = require('./questionsProgrammingWeb');
const networkingAndDatabases = require('./questionsNetworkingDatabases');
const securityAndSystems = require('./questionsSecuritySystems');
const aiAndSoftware = require('./questionsAiAndSoftware');

module.exports = [
  ...programmingAndWeb,
  ...networkingAndDatabases,
  ...securityAndSystems,
  ...aiAndSoftware,
];
