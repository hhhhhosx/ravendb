import database = require("models/resources/database");
import commandBase = require("commands/commandBase");
import endpoints = require("endpoints");

class saveTimeSeriesCommand extends commandBase {
    constructor(private documentId: string, private name: string, private dto: Raven.Client.Documents.Operations.TimeSeries.TimeSeriesOperation.AppendOperation, 
                private db: database) {
        super();
    }
    
    execute(): JQueryPromise<void> {
        const args = {
            docId: this.documentId
        };

        const url = endpoints.databases.timeSeries.timeseries + this.urlEncodeArgs(args);
        
        const payload = {
            Name: this.name,
            Removals: [],
            Appends: [
                this.dto
            ]
        } as Raven.Client.Documents.Operations.TimeSeries.TimeSeriesOperation;
        
        return this.post(url, JSON.stringify(payload), this.db, { dataType: undefined })
            .fail((response: JQueryXHR) => this.reportError("Failed to save time series.", response.responseText, response.statusText));
    }
}

export = saveTimeSeriesCommand;
